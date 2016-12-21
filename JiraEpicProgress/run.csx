#load "..\extensions\JiraEx.csx"

using Dapper;
using System;
using System.Configuration;
using System.Data.SqlClient;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Threading.Tasks;
using Newtonsoft.Json;

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log)
{
    var now = DateTime.UtcNow;
    var createdOn = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
    var cnnString = ConfigurationManager.ConnectionStrings["SqlConnection"].ConnectionString;
    using (var cnn = new SqlConnection(cnnString))
    {
        cnn.Open();
        var existing = cnn.Query<JiraEpicProgress>("select CreatedOn = @CreatedOn", new { CreatedOn = createdOn });
        log.Info($"Pulled {existing.Count()} JiraEpicProgress from database OK.");
    }

    string serviceUrl = ConfigurationManager.AppSettings["Jira.ServiceUrl"];
    string username = ConfigurationManager.AppSettings["Jira.Username"];
    string password = ConfigurationManager.AppSettings["Jira.Password"];

    // Get Epics linked to teams based on a status
    var jql = "type=epic and \"Scrum Team\" in (Faust, Mako, Dropbear, Esperanto, Morpheus, Indy) and status in (\"in progress\")";
    var epics = JiraEx.Get<IEnumerable<Issue>>(serviceUrl, $"/rest/api/2/search?jql={jql}&fields=summary,project,resolution,status&maxResults=250", username, password, "issues")
        .ToList();

    log.Info($"Found {epics.Count()} epics");

    var graph = new Graph { Title = "Epic Progress", DataSequences = new List<GraphSequence>() };

    var epicProgress = new List<JiraEpicProgress>();
    foreach (var epic in epics)
    {
        var issues = JiraEx.Get<IEnumerable<Issue>>(serviceUrl, $"/rest/agile/1.0/epic/{epic.Key}/issue?fields=project,resolution,status", username, password, "issues")
            .ToList();
        var resolved = issues.GroupBy(i => i.IsResolved)
            .Select(group => new
            {
                Resolved = group.Key,
                Count = group.Count()
            });

        var progress = new JiraEpicProgress
        {
            JiraId = epic.Key,
            CreatedOn = createdOn,
            EpicName = epic.Fields.Summary,
            Resolved = resolved.FirstOrDefault(v => v.Resolved)?.Count,
            InProgress = resolved.FirstOrDefault(v => !v.Resolved)?.Count
        };
        epicProgress.Add(progress);

        //var sequence = new GraphSequence { Title = epic.Fields?.Summary, DataPoints = new List<GraphPoint>() };
        //sequence.DataPoints.Add(new GraphPoint { Title = "Resolved", Value = resolved.FirstOrDefault(v => v.Resolved)?.Count });
        //sequence.DataPoints.Add(new GraphPoint { Title = "In Progress", Value = resolved.FirstOrDefault(v => !v.Resolved)?.Count });

        //graph.DataSequences.Add(sequence);

        log.Info($"{sequence.Title} done");
    }

    using (var cnn = new SqlConnection(cnnString))
    {
        cnn.Open();

        foreach (var progress in epicProgress)
        {
            cnn.Execute("insert JiraEpicProgress(EpicName, CreatedOn, Resolved, InProgress, JiraId) values(@epicName, @createdOn, @resolved, @inProgress, @jiraId)",
                new { progress.EpicName, progress.CreatedOn, progress.Resolved, progress.InProgress, progress.JiraId }
            );
        }
        log.Info("Log added to database successfully!");
    }

    string json = JsonConvert.SerializeObject(graph);
    return Message(req, json);
}

static HttpResponseMessage Message(HttpRequestMessage req, string message)
{
    return req.CreateResponse(HttpStatusCode.OK, message, JsonMediaTypeFormatter.DefaultMediaType);
}

class Board
{
    public int Id { get; set; }
    public string Self { get; set; }
    public string Name { get; set; }
    public string Type { get; set; }
}

class Epic
{
    public int Id { get; set; }
    public string Self { get; set; }
    public string Name { get; set; }
    public string Summary { get; set; }
    public bool Done { get; set; }
}

class Issue
{
    public string Key { get; set; }

    public string Status => Fields?.Status?.Name;
    public int? StatusId => Fields?.Status?.Id;
    public string Project => Fields?.Project?.Key;
    public bool IsResolved => Fields?.Resolution?.IsResolved ?? false;

    public Fields Fields { get; set; }
}

class Fields
{
    public string Summary { get; set; }
    public Status Status { get; set; }
    public Project Project { get; set; }
    public Resolution Resolution { get; set; }
}

class Status
{
    public string Name { get; set; }
    public int Id { get; set; }
}

class Project
{
    public string Key { get; set; }
}

class ColumnConfig
{
    public IEnumerable<Columns> Columns { get; set; }
}

class Columns
{
    public string Name { get; set; }
    public IEnumerable<ColumnStatus> Statuses { get; set; }
}

class ColumnStatus
{
    public int Id { get; set; }
    public string Self { get; set; }
}

class Resolution
{
    static IEnumerable<string> Resolved = new List<string> { "Fixed", "Done", "Closed", "Won't do" };

    public int Id { get; set; }
    public string Name { get; set; }

    public bool IsResolved => Resolved.Any(res => Name.Equals(res, StringComparison.OrdinalIgnoreCase));
}

/* Dash graph */
class Graph
{
    public string Title { get; set; }
    public IList<GraphSequence> DataSequences { get; set; }
}

class GraphSequence
{
    public string Title { get; set; }
    public IList<GraphPoint> DataPoints { get; set; }
}

class GraphPoint
{
    public String Title { get; set; }
    public decimal? Value { get; set; }
}

/* Database */

public class JiraEpicProgress
{
    public int Id { get; set; }
    public string EpicName { get; set; }
    public DateTime CreatedOn { get; set; }
    public int? Resolved { get; set; }
    public int? InProgress { get; set; }
    public string JiraId { get; set; }
} 