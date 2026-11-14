
using System;
using System.IO;
using System.Xml;
using System.Text;
using System.Collections.Specialized;
using Microsoft.MetadirectoryServices;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Data;
//using SAPLibrary;

namespace FimSync_Ezma
{
    public class EzmaExtension :
        IMAExtensible2CallExport,
        IMAExtensible2CallImport,
        IMAExtensible2GetSchema,
        IMAExtensible2GetCapabilities,
        IMAExtensible2GetParameters
    {
        string datePortalFormat;
        //
        // Constructor
        //
        public EzmaExtension()
        {
            datePortalFormat = "yyyy-MM-ddTHH:mm:ss.000";
            //
            // TODO: Add constructor logic here
            //
        }

        /// <summary>
        /// Implementace IMAExtensible2GetCapabilities
        /// Možnosti MA pro konfiguračního průvodce
        /// </summary>
        public MACapabilities Capabilities
        {
            get
            {
                MACapabilities myCapabilites = new MACapabilities
                {
                    ConcurrentOperation = true,
                    DeleteAddAsReplace = false,
                    DeltaImport = false,
                    DistinguishedNameStyle = MADistinguishedNameStyle.None,
                    ExportPasswordInFirstPass = false,
                    ExportType = MAExportType.AttributeUpdate,
                    FullExport = true,
                    NoReferenceValuesInFirstExport = false,
                    IsDNAsAnchor = false,
                    Normalizations = MANormalizations.None,
                    ObjectConfirmation = MAObjectConfirmation.NoAddAndDeleteConfirmation,
                    ObjectRename = false,
                    SupportExport = false,
                    SupportHierarchy = false,
                    SupportImport = true,
                    SupportPartitions = false,
                    SupportPassword = false
                };
                return myCapabilites;
            }
        }

        /// <summary>
        /// Implementace IMAExtensible2GetParameters
        /// Konfigurovatelné parametry MA pro průvodce
        /// </summary>
        /// <param name="configParameters"></param>
        /// <param name="page"></param>
        /// <returns></returns>
        public System.Collections.Generic.IList<ConfigParameterDefinition> GetConfigParameters(System.Collections.ObjectModel.KeyedCollection<string, ConfigParameter> configParameters, ConfigParameterPage page)
        {
            List<ConfigParameterDefinition> configParametersDefinitions = new List<ConfigParameterDefinition>();

            switch (page)
            {
                case ConfigParameterPage.Connectivity:

                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateStringParameter("Server host", ""));
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateStringParameter("System ID", ""));
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateStringParameter("User", ""));
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateEncryptedStringParameter("Password", ""));
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateEncryptedStringParameter("LogFile", ""));
                    break;



                case ConfigParameterPage.Global:
                    //configParametersDefinitions.Add(ConfigParameterDefinition.CreateStringParameter("Kód organizace", ""));
                    //configParametersDefinitions.Add(ConfigParameterDefinition.CreateCheckBoxParameter("Před importem spustit výpočet v SAP"));
                    break;

                case ConfigParameterPage.Partition:
                    break;

                case ConfigParameterPage.RunStep:

                    break;
            }

            return configParametersDefinitions;

        }

        /// <summary>
        /// Implementace IMAExtensible2GetParameters
        /// Validace parametrů, nevyužito
        /// </summary>
        /// <param name="configParameters"></param>
        /// <param name="page"></param>
        /// <returns></returns>
        public ParameterValidationResult ValidateConfigParameters(System.Collections.ObjectModel.KeyedCollection<string, ConfigParameter> configParameters, ConfigParameterPage page)
        {
            ParameterValidationResult myResults = new ParameterValidationResult();

            return myResults;
        }

        /// <summary>
        /// Implementace IMAExtensible2GetSchema
        /// Datové schéma ConnectorSpace
        /// </summary>
        /// <param name="configParameters"></param>
        /// <returns></returns>
        public Schema GetSchema(System.Collections.ObjectModel.KeyedCollection<string, ConfigParameter> configParameters)
        {
            Microsoft.MetadirectoryServices.SchemaType personType = Microsoft.MetadirectoryServices.SchemaType.Create("Person", false);
            personType.Attributes.Add(SchemaAttribute.CreateAnchorAttribute("EmployeeID", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("Firstname", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("Lastname", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("FullName", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("ValidFrom", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("ValidTo", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("NickName", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("EMail", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("LastModificationDate", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("ModDate", AttributeType.String));
            personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("LTime", AttributeType.String));

            Schema schema = Schema.Create();
            schema.Types.Add(personType);

            Microsoft.MetadirectoryServices.SchemaType groupType = Microsoft.MetadirectoryServices.SchemaType.Create("Group", false);

            groupType.Attributes.Add(SchemaAttribute.CreateAnchorAttribute("ID", AttributeType.String));
            groupType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("EmployeeID", AttributeType.String));
            groupType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("ValidFrom", AttributeType.String));
            groupType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("ValidTo", AttributeType.String));
            groupType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("Description", AttributeType.String));
            schema.Types.Add(groupType);
            return schema;
        }

        /// //////////////////////////////////////////////////////////////////////////////////////////////////////
        /// IMPORT
        /// //////////////////////////////////////////////////////////////////////////////////////////////////////

        //SAPReference.ZSKSEHR_SERVICE_MIMClient client;
        int pageSize;
        int importCount = 0;
        //List<Person> persons = new List<Person>();
        public string logFile = null;

        /// <summary>
        /// Spuštění importu. Je zavolána webová služba a stažena data.
        /// Data uložena do privátní proměnné pro další zpracování.
        /// </summary>
        /// <param name="configParameters"></param>
        /// <param name="types"></param>
        /// <param name="importRunStep"></param>
        /// <returns></returns>
        public OpenImportConnectionResults OpenImportConnection(System.Collections.ObjectModel.KeyedCollection<string, ConfigParameter> configParameters, Schema types, OpenImportConnectionRunStep importRunStep)
        {
            // var cfg = new Configuration();
            //RfcDestinationManager.RegisterDestinationConfiguration(cfg);
            //SAPUtils sap = new SAPUtils();
            logFile = configParameters["LogFile"].Value;
            //try
            //{
            //  //  persons = sap.GetAll();

            //}
            //catch (Exception ex)
            //{
            //    throw new Exception(ex.Message + "\n" + ex.StackTrace);
            //}

            pageSize = importRunStep.PageSize;
            return new OpenImportConnectionResults();
        }

        /// <summary>
        /// Zpracování záznamů. Pracuje po "stránkách", musí. Maximální počet záznamů,
        /// které může tato metoda zpracovat v jednom běhu je 9999. Pokud není import
        /// dokončen, je nastaveno výstupní info MoreToImport=True
        /// </summary>
        /// <param name="importRunStep"></param>
        /// <returns></returns>
        public GetImportEntriesResults GetImportEntries(GetImportEntriesRunStep importRunStep)
        {
            GetImportEntriesResults importReturnInfo = new GetImportEntriesResults();
            return importReturnInfo;

        }
        private string ChangeFormat(string date, string fromFormat, string toFormat, bool IsEndDate=false)
        {
            DateTime dt;
            if (!DateTime.TryParseExact(date, fromFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt))
            {
                return "";
            }
            if (IsEndDate)
                dt = dt.AddHours(23).AddMinutes(59);
            return dt.ToString(toFormat);
        }

        private DateTime getDate(string date, string fromFormat, DateTime current, bool IsEndDate = false)
        {
            DateTime dt;
            if (!DateTime.TryParseExact(date, fromFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt))
            {
                if (IsEndDate)
                    current = current.AddHours(23).AddMinutes(59);

                return current;
            }
            if (IsEndDate)
                dt = dt.AddHours(23).AddMinutes(59);
            return dt;
        }

        /// <summary>
        /// Ukončení importu, nevyužito
        /// </summary>
        /// <param name="importRunStep"></param>
        /// <returns></returns>
        public CloseImportConnectionResults CloseImportConnection(CloseImportConnectionRunStep importRunStep)
        {
            return new CloseImportConnectionResults();
        }

        /// <summary>
        /// Výchozí velikost stránky pro průvodce Sync Run
        /// </summary>
        public int ImportDefaultPageSize
        {
            get { return 1000; }
        }

        /// <summary>
        /// Maximální povolená velikost stránky
        /// </summary>
        public int ImportMaxPageSize
        {
            get { return 1000; }
        }

        /// <summary>
        /// Pomocná metoda pro decryptování hesla. Využívá se při volání webové služby.
        /// Heslo je v konfiguraci uloženo šifrované.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        String SecureStringToString(System.Security.SecureString value)
        {
            IntPtr valuePtr = IntPtr.Zero;
            try
            {
                valuePtr = System.Runtime.InteropServices.Marshal.SecureStringToGlobalAllocUnicode(value);
                return System.Runtime.InteropServices.Marshal.PtrToStringUni(valuePtr);
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.ZeroFreeGlobalAllocUnicode(valuePtr);
            }
        }

        /// //////////////////////////////////////////////////////////////////////////////////////////////////////
        /// EXPORT
        /// //////////////////////////////////////////////////////////////////////////////////////////////////////

        public void CloseExportConnection(CloseExportConnectionRunStep exportRunStep)
        {

        }

        public int ExportDefaultPageSize
        {
            get { return 1000; }
        }

        public int ExportMaxPageSize
        {
            get { return 1000; }
        }

        public void OpenExportConnection(System.Collections.ObjectModel.KeyedCollection<string, ConfigParameter> configParameters, Schema types, OpenExportConnectionRunStep exportRunStep)
        {
            //SetUpWSClient(configParameters);
        }

        public PutExportEntriesResults PutExportEntries(IList<CSEntryChange> csentries)
        {
            /*
            List<ZhrIdmSZamIdentifikace> targets = new List<ZhrIdmSZamIdentifikace>();

            foreach (CSEntryChange item in csentries.Where(x => x.ObjectType == "Person" && x.ObjectModificationType == ObjectModificationType.Update))
            {
                var target = new ZhrIdmSZamIdentifikace();
                var targetChanges = new List<ZhrIdmSIdentifikace>();

                string[] targetAttrName = { "PrihlasovaciJmeno", "CelePrihlasovaciJmeno", "Email", "TelefonniCislo", "MobilniCislo" };

                foreach (string attribName in item.ChangedAttributeNames.Where(x => targetAttrName.Contains(x)))
                {
                    string attribValue = "";
                    target.OsobniCislo = item.AnchorAttributes[0].Value.ToString();
                    switch (item.AttributeChanges[attribName].ModificationType)
                    {
                        case AttributeModificationType.Add:
                            attribValue = item.AttributeChanges[attribName].ValueChanges[0].Value.ToString().ToLower();
                            targetChanges.Add(new ZhrIdmSIdentifikace { CilovePole = attribName, Identifikace = attribValue });
                            break;
                        case AttributeModificationType.Update:
                            attribValue = item.AttributeChanges[attribName].ValueChanges.First(x => x.ModificationType == ValueModificationType.Add).Value.ToString().ToLower();
                            targetChanges.Add(new ZhrIdmSIdentifikace { CilovePole = attribName, Identifikace = attribValue });
                            break;
                        case AttributeModificationType.Delete:
                            targetChanges.Add(new ZhrIdmSIdentifikace { CilovePole = attribName, Identifikace = "" });
                            break;
                        default:
                            throw new NotImplementedException("ModificationType=" + item.AttributeChanges[attribName].ModificationType.ToString());
                    }
                }

                if (targetChanges.Count > 0)
                {
                    target.Identifikace = targetChanges.ToArray();
                    targets.Add(target);
                }
            }

            if (targets.Count > 0)
            {
                ZHrIdmRzhZapisIdOsoby reqData2 = new ZHrIdmRzhZapisIdOsoby();
                reqData2.ZamIdentifikace = targets.ToArray();

                var resp = client.ZHrIdmRzhZapisIdOsoby(reqData2);

                bool isError = false;
                string errMessage = "";
                foreach (var returnItem in resp.ZamReturn)
                {
                    errMessage = "OsobniCislo=" + returnItem.OsobniCislo + "\r\n";
                    foreach (var item2 in returnItem.IdentReturn)
                    {
                        if (item2.ReturnCode != 0)
                        {
                            isError = true;
                            errMessage += item2.CilovePole + " ReturnCode=" + item2.ReturnCode.ToString() + "\r\n";
                            errMessage += string.Join("", item2.Messages.Select(x => "Id=" + x.Id + " Num=" + x.Number + " Type=" + x.Type + "\r\n" + x.Message + "\r\n").ToArray());
                        }
                    }
                }

                if (isError)
                {
                    throw new ExtensibleExtensionException(errMessage);
                }
            }
            else
            {
                throw new EntryExportException("Nebyly vyexportovány žádné záznamy.");
            }
            */
            PutExportEntriesResults exportEntriesResults = new PutExportEntriesResults();
            return exportEntriesResults;

        }
    }
}
