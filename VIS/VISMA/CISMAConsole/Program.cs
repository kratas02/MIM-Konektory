using FimSync_Ezma;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CISMAConsole
{
    internal class Program
    {
        private const string User = "idm";
        private const string Pass = "";

        static async Task Main(string[] args)
        {
            var uri1 = "http://crow.test.local:5003/api/v1/Custom/Idm/Users";
            IdmUser[] users1 = await GetUsersAsync(uri1, User, Pass);
            Console.WriteLine($"Test: {users1.Length} uživatelů");
            foreach (var user in users1)
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

        static async Task<IdmUser[]> GetUsersAsync(string uri, string user, string pass)
        {
            var client = new HttpClient();
            var credentials = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{user}:{pass}"));
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", credentials);

            var response = await client.GetAsync(uri);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var users = JsonSerializer.Deserialize<IdmUser[]>(json, options);
            return users ?? Array.Empty<IdmUser>();
        }

    }
}