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
    string notReviewed = req.GetQueryNameValuePairs()
        .FirstOrDefault(q => string.Compare(q.Key, "not-reviewed", true) == 0)
        .Value;

    DateTime tryDate;
    int tryDays = 0;
    if (DateTime.TryParse(from, out tryDate))
    {
        dFrom = tryDate;
    }
    else if (int.TryParse(from, out tryDays))
    {
        dFrom = dFrom.Subtract(TimeSpan.FromDays(Math.Abs(tryDays)));
    }

    string tfsPath = ConfigurationManager.AppSettings["Tfs.Path"] ?? "$/";

    var tickets = TfsEx.GetHistory(log, tfsPath, dFrom);
    if (!string.IsNullOrWhiteSpace(notReviewed))
    {
        tickets = TfsEx.NotReviewed(log, history); // Filter for not reviewed items
    }

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