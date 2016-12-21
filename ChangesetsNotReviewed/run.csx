#load "..\extensions\TfsEx.csx"

using System;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log)
{
    DateTime dFrom = DateTime.Now.Subtract(TimeSpan.FromDays(1));
    string from = req.GetQueryNameValuePairs()
        .FirstOrDefault(q => string.Compare(q.Key, "from", true) == 0)
        .Value;

    int days = 0;
    if (DateTime.TryParse(from, out dFrom))
    {
        // i guess that worked
    }
    else if (int.TryParse(from, out days))
    {
        dFrom = dFrom.Subtract(TimeSpan.FromDays(Math.Abs(days)));
    }

    string tfsPath = ConfigurationManager.AppSettings["Tfs.Path"] ?? "$/";

    var tickets = TfsEx.GetHistory(log, tfsPath, dFrom);

    string message = tickets.Count().ToString();
    return Message(req, message);
}

static HttpResponseMessage Message(HttpRequestMessage req, string message)
{
    return req.CreateResponse(HttpStatusCode.OK, new
    {
        text = message
    });
}