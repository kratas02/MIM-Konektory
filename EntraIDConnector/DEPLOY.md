# EntraID MIM Connector – Nasazení

## Předpoklady

- MIM 2016 (Synchronization Service)
- .NET Framework 4.6.2
- Aplikace registrovaná v Entra ID s oprávněním `User.Read.All` (application permission)

---

## 1. Oprávnění aplikace v Entra ID

V Azure Portal → App registrations → tvoje aplikace → API permissions:

| API            | Oprávnění      | Typ         |
|----------------|----------------|-------------|
| Microsoft Graph | User.Read.All | Application |

Po přidání klikni **Grant admin consent**.

---

## 2. Sestavení DLL

```powershell
# Nastav cestu k MIM assemblies
$env:MIM_SYNC_PATH = "C:\Program Files\Microsoft Forefront Identity Manager\2010\Synchronization Service\Bin\Assemblies"

cd C:\Projects\MIM\EntraIDConnector\src
dotnet restore
dotnet build -c Release
```

Výstup: `bin\Release\net462\EntraIDConnector.dll`

---

## 3. Nasazení DLL

Zkopíruj do složky Extensions na MIM Sync serveru:

```powershell
$mimExtensions = "C:\Program Files\Microsoft Forefront Identity Manager\2010\Synchronization Service\Extensions"

Copy-Item "bin\Release\net462\EntraIDConnector.dll"         -Destination $mimExtensions
Copy-Item "bin\Release\net462\Newtonsoft.Json.dll"          -Destination $mimExtensions
```

---

## 4. Vytvoření Management Agenta v MIM

1. Synchronization Service Manager → **Management Agents** → **Create**
2. Typ: **Extensible Connectivity 2.0**
3. DLL: `EntraIDConnector.dll`
4. Vyplň konfigurační parametry:

| Parametr     | Hodnota                              |
|--------------|--------------------------------------|
| TenantId     | `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx` |
| ClientId     | `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx` |
| ClientSecret | `*****` (šifrováno v MIM)            |
| ProxyUrl     | `http://proxy.firma.cz:8080` (nebo prázdné) |

5. **Configure Object Types**: zaškrtni `Person`
6. **Configure Attributes**: vyber požadované atributy
7. **Configure Anchors**: `id`

---

## 5. Spuštění importů

### První spuštění (Full Import)
```
MA → Run → Full Import (Stage Only)
```
Trvá dle počtu uživatelů. Po dokončení se uloží DeltaLink jako watermark.

### Pravidelný Delta Import
```
MA → Run → Delta Import (Stage Only)
```
Načte pouze změny od posledního běhu.

---

## 6. Watermark a delta mechanismus

```
Full Import:
  GET /users/delta?$select=...
     ↓ stránkování přes @odata.nextLink
  GET /users/delta?$skiptoken=...
     ↓ poslední stránka
  @odata.deltaLink → uložen jako CustomData (watermark)

Delta Import:
  GET {uložený deltaLink}
     ↓ vrátí pouze změny od minulého běhu
  nový @odata.deltaLink → přepíše watermark
```

---

## 7. Přidání vlastních atributů

V `GraphApiClient.cs` uprav konstantu `SelectFields`:
```csharp
public const string SelectFields =
    "id,userPrincipalName,displayName,...,tvůjAtribut";
```

V `EntraIDConnector.cs` přidej atribut do pole `SingleValuedStringAttributes`:
```csharp
private static readonly string[] SingleValuedStringAttributes =
{
    // ... existující ...
    "tvůjAtribut",
};
```

A v `GetSchema()` se přidá automaticky.

---

## 8. Diagnostika chyb

| Chyba | Příčina | Řešení |
|-------|---------|--------|
| `Nepodařilo se získat access token` | Špatný clientId/secret/tenantId | Ověř v Azure Portal |
| `Graph API vrátilo 403` | Chybí `User.Read.All` nebo admin consent | Přidej oprávnění |
| `Graph API vrátilo 407` | Proxy vyžaduje autentizaci | Nastav ProxyUrl včetně credentialů |
| Delta import bez watermarku | Předchozí Full Import neskončil úspěšně | Spusť Full Import znovu |
