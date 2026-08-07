using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using VIS;

// Source - https://stackoverflow.com/a
// Posted by Karol Berezicki
// Retrieved 2025-12-05, License - CC BY-SA 4.0

class VISUsers
{
    private const string User = "idm";
    private const string Pass = "pass";

    static void Main(string[] args)
    {
        // První volání - test.local
        var uri1 = "http://crow.test.local:5003/api/v1/Custom/Idm/Users";
        IdmUser[] users1 = GetUsers(uri1, User, Pass);
        Console.WriteLine($"Test: {users1.Length} uživatelů");
        foreach (var user in users1)
            VypisUzivatele(user);

        // Druhé volání - asol.local
        var uri2 = "http://crow.asol.local:5003/api/v1/Custom/Idm/Users";
        IdmUser[] users2 = GetUsers(uri2, User, Pass);
        Console.WriteLine($"Asol: {users2.Length} uživatelů");
        foreach (var user in users2)
            VypisUzivatele(user);
    }

    static void VypisUzivatele(IdmUser user)
    {
        foreach (var prop in typeof(IdmUser).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.Name == nameof(IdmUser.ExtensionData))
            {
                if (user.ExtensionData?.Count > 0)
                {
                    Console.WriteLine($"  {prop.Name}:");
                    foreach (var kv in user.ExtensionData)
                        Console.WriteLine($"    {kv.Key} = {kv.Value}");
                }
                continue;
            }
            var value = prop.GetValue(user);
            Console.WriteLine($"  {prop.Name}: {value ?? "(null)"}");
        }
        Console.WriteLine();
    }

    static IdmUser[] GetUsers(string uri, string user, string pass)
    {
        using var client = new HttpClient();
        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{user}:{pass}"));
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);

        var response = client.GetAsync(uri).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();

        var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var users = JsonSerializer.Deserialize<IdmUser[]>(json, options);
        return users ?? Array.Empty<IdmUser>();
    }
}
