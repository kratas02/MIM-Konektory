using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.MetadirectoryServices;
using Newtonsoft.Json.Linq;

namespace EntraID.MIM.Connector
{
    /// <summary>
    /// MIM ECMA2 konektor pro Microsoft Entra ID (Azure AD).
    /// Podporuje Full Import a Delta Import přes Microsoft Graph API /users/delta.
    /// Export není implementován – konektor je read-only.
    /// </summary>
    public sealed class EntraIDConnector :
        IMAExtensible2GetCapabilities,
        IMAExtensible2GetSchema,
        IMAExtensible2GetParameters,
        IMAExtensible2CallImport
    {
        // ── Konstanty konfiguračních parametrů ─────────────────────────────

        private const string ParamTenantId     = "TenantId";
        private const string ParamClientId     = "ClientId";
        private const string ParamClientSecret = "ClientSecret";
        private const string ParamProxyUrl     = "ProxyUrl";

        // ── Stav instance (životnost = jeden import běh) ────────────────────

        private GraphApiClient _graphClient;
        private OperationType  _importType;

        // ═══════════════════════════════════════════════════════════════════
        // IMAExtensible2GetCapabilities
        // ═══════════════════════════════════════════════════════════════════

        public int ImportDefaultPageSize => 100;
        public int ImportMaxPageSize     => 999;

        public MACapabilities Capabilities => new MACapabilities
        {
            SupportImport              = true,
            SupportExport              = false,
            SupportPassword            = false,
            SupportPartitions          = false,
            SupportHierarchy           = false,
            DeltaImport                = true,
            FullExport                 = false,
            ObjectRename               = false,
            DeleteAddAsReplace         = false,
            ConcurrentOperation        = false,
            NoReferenceValuesInFirstExport = false,
            ExportPasswordInFirstPass  = false,
            // DN jako anchor: MIM použije entry.DN jako identifikátor objektu.
            // Tím odpadá nutnost ručně nastavovat AnchorAttributes.
            IsDNAsAnchor               = true,
            DistinguishedNameStyle     = MADistinguishedNameStyle.Generic,
            Normalizations             = MANormalizations.None,
            ExportType                 = MAExportType.AttributeUpdate,
        };

        // ═══════════════════════════════════════════════════════════════════
        // IMAExtensible2GetParameters
        // ═══════════════════════════════════════════════════════════════════

        public IList<ConfigParameterDefinition> GetConfigParameters(
            KeyedCollection<string, ConfigParameter> configParameters,
            ConfigParameterPage page)
        {
            var list = new List<ConfigParameterDefinition>();

            if (page == ConfigParameterPage.Connectivity)
            {
                list.Add(ConfigParameterDefinition.CreateStringParameter(
                    ParamTenantId, string.Empty));

                list.Add(ConfigParameterDefinition.CreateStringParameter(
                    ParamClientId, string.Empty));

                list.Add(ConfigParameterDefinition.CreateEncryptedStringParameter(
                    ParamClientSecret, string.Empty));

                list.Add(ConfigParameterDefinition.CreateDividerParameter());

                list.Add(ConfigParameterDefinition.CreateStringParameter(
                    ParamProxyUrl, string.Empty));

                list.Add(ConfigParameterDefinition.CreateLabelParameter(
                    "Proxy URL format: http://proxy.firma.cz:8080  (leave empty if no proxy needed)"));
            }

            return list;
        }

        public ParameterValidationResult ValidateConfigParameters(
            KeyedCollection<string, ConfigParameter> configParameters,
            ConfigParameterPage page)
        {
            if (page == ConfigParameterPage.Connectivity)
            {
                if (string.IsNullOrWhiteSpace(configParameters[ParamTenantId].Value))
                    return new ParameterValidationResult(
                        ParameterValidationResultCode.Failure, "TenantId nesmí být prázdný.", ParamTenantId);

                if (string.IsNullOrWhiteSpace(configParameters[ParamClientId].Value))
                    return new ParameterValidationResult(
                        ParameterValidationResultCode.Failure, "ClientId nesmí být prázdný.", ParamClientId);
            }

            return new ParameterValidationResult(ParameterValidationResultCode.Success, string.Empty, string.Empty);
        }

        // ═══════════════════════════════════════════════════════════════════
        // IMAExtensible2GetSchema
        // ═══════════════════════════════════════════════════════════════════

        public Schema GetSchema(KeyedCollection<string, ConfigParameter> configParameters)
        {
            var schema     = Schema.Create();
            var personType = SchemaType.Create("Person", false);

            // Jednodnotné řetězcové atributy (id je DN = anchor via IsDNAsAnchor)
            foreach (var name in SingleValuedStringAttributes)
            {
                personType.Attributes.Add(
                    SchemaAttribute.CreateSingleValuedAttribute(name, AttributeType.String));
            }

            // Boolean atributy
            personType.Attributes.Add(
                SchemaAttribute.CreateSingleValuedAttribute("accountEnabled", AttributeType.Boolean));

            // Vícehodnotové atributy
            personType.Attributes.Add(
                SchemaAttribute.CreateMultiValuedAttribute("businessPhones", AttributeType.String));

            schema.Types.Add(personType);
            return schema;
        }

        private static readonly string[] SingleValuedStringAttributes =
        {
            "id",
            "userPrincipalName",
            "displayName",
            "givenName",
            "surname",
            "mail",
            "employeeId",
            "department",
            "jobTitle",
            "mobilePhone",
            "officeLocation",
            "usageLocation",
            "companyName",
            "createdDateTime",
            "userType",
            "onPremisesSamAccountName",
            "onPremisesImmutableId",
            "onPremisesDistinguishedName",
        };

        // ═══════════════════════════════════════════════════════════════════
        // IMAExtensible2CallImport
        // ═══════════════════════════════════════════════════════════════════

        public OpenImportConnectionResults OpenImportConnection(
            KeyedCollection<string, ConfigParameter> configParameters,
            Schema types,
            OpenImportConnectionRunStep importRunStep)
        {
            var tenantId     = configParameters[ParamTenantId].Value;
            var clientId     = configParameters[ParamClientId].Value;
            var clientSecret = GraphApiClient.SecureStringToString(
                                   configParameters[ParamClientSecret].SecureValue);
            var proxyUrl     = configParameters[ParamProxyUrl].Value;

            _graphClient = new GraphApiClient(tenantId, clientId, clientSecret, proxyUrl);
            _importType  = importRunStep.ImportType;

            string savedDeltaLink = importRunStep.CustomData;

            if (_importType == OperationType.Delta && !string.IsNullOrEmpty(savedDeltaLink))
            {
                _graphClient.InitDeltaImport(savedDeltaLink);
            }
            else
            {
                // Full import nebo první spuštění – použij delta query od začátku.
                // Tím získáme DeltaLink na konci průchodu pro budoucí delta importy.
                _graphClient.InitFullImport();
            }

            return new OpenImportConnectionResults();
        }

        public GetImportEntriesResults GetImportEntries(GetImportEntriesRunStep importRunStep)
        {
            var result = new GetImportEntriesResults
            {
                CSEntries    = new List<CSEntryChange>(),
                MoreToImport = false,
            };

            if (!_graphClient.HasMorePages)
                return result;

            // Načti jednu stránku (blokující call nad async metodou)
            var users = _graphClient.GetNextPageAsync().GetAwaiter().GetResult();

            foreach (var user in users)
            {
                var entry = BuildCSEntryChange(user);
                if (entry != null)
                    result.CSEntries.Add(entry);
            }

            result.MoreToImport = _graphClient.HasMorePages;
            return result;
        }

        public CloseImportConnectionResults CloseImportConnection(
            CloseImportConnectionRunStep importRunStep)
        {
            // Ulož delta token jako watermark pro příští delta import
            var deltaLink = _graphClient?.DeltaLink ?? string.Empty;

            _graphClient?.Dispose();
            _graphClient = null;

            return new CloseImportConnectionResults(deltaLink);
        }

        // ═══════════════════════════════════════════════════════════════════
        // Privátní logika – sestavení CSEntryChange
        // ═══════════════════════════════════════════════════════════════════

        private CSEntryChange BuildCSEntryChange(JObject user)
        {
            if (user == null) return null;

            var objectId = user["id"]?.ToString();
            if (string.IsNullOrEmpty(objectId)) return null;

            // Graph delta označuje smazané objekty pomocí @removed
            bool isDeleted = user.ContainsKey("@removed");

            // Při Full Importu přeskočíme smazané objekty – v CS ještě neexistují,
            // MIM by nedokázal zpracovat Delete → "missing-change-type"
            if (isDeleted && _importType != OperationType.Delta)
                return null;

            var entry = CSEntryChange.Create();
            entry.ObjectType = "Person";

            // IsDNAsAnchor = true: DN slouží jako anchor, žádné AnchorAttributes nejsou potřeba.
            // Entra ID object ID GUID je globálně unikátní – ideální jako DN/anchor.
            entry.DN = objectId;

            if (isDeleted)
            {
                // Delta import: smaž objekt z CS podle DN (= anchor)
                entry.ObjectModificationType = ObjectModificationType.Delete;
                return entry;
            }

            // Full import → Add; Delta import → Replace
            entry.ObjectModificationType = _importType == OperationType.Delta
                ? ObjectModificationType.Replace
                : ObjectModificationType.Add;

            // ── Jednodnotné řetězcové atributy ─────────────────────────────
            foreach (var attrName in SingleValuedStringAttributes)
            {
                var token = user[attrName];
                if (token == null || token.Type == JTokenType.Null) continue;

                var value = token.ToString();
                if (!string.IsNullOrEmpty(value))
                {
                    entry.AttributeChanges.Add(
                        AttributeChange.CreateAttributeAdd(attrName, value));
                }
            }

            // ── Boolean: accountEnabled ─────────────────────────────────────
            var accountEnabledToken = user["accountEnabled"];
            if (accountEnabledToken != null && accountEnabledToken.Type != JTokenType.Null)
            {
                entry.AttributeChanges.Add(
                    AttributeChange.CreateAttributeAdd(
                        "accountEnabled",
                        accountEnabledToken.ToObject<bool>()));
            }

            // ── Vícehodnotové: businessPhones ────────────────────────────────
            if (user["businessPhones"] is JArray phones && phones.Count > 0)
            {
                var phoneList = phones
                    .Where(p => p.Type != JTokenType.Null)
                    .Select(p => (object)p.ToString())
                    .ToList();

                if (phoneList.Count > 0)
                {
                    entry.AttributeChanges.Add(
                        AttributeChange.CreateAttributeAdd("businessPhones", phoneList));
                }
            }

            return entry;
        }
    }
}
