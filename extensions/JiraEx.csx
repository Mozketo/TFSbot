#r "Newtonsoft.Json"

using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

internal class TfsEx
{
    /// <summary>
    /// Sends a request to the Jira API with (string) method and the (string) url.
    /// Uses HttpWebRequest and StreamReader to read the body of the request and return a string.
    /// </summary>
    /// <returns>String</returns>
    internal static T Get<T>(string baseUrl, string urlPath, string username, string password, string jsonToken = "values")
    {
        var authValue = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));

        var client = new HttpClient(new HttpClientHandler() { UseDefaultCredentials = true })
        {
            DefaultRequestHeaders = { Authorization = authValue },
            BaseAddress = new Uri(baseUrl)
        };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var uri = new Uri(client.BaseAddress, urlPath);
        var response = client.GetAsync(uri).Result;

        response.EnsureSuccessStatusCode();

        string content = response.Content.ReadAsStringAsync().Result;

        var token = JObject.Parse(content);
        var result = token.SelectToken(jsonToken).ToObject<T>();
        return result;
    }
}