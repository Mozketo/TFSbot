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
using System.Net.Http.Headers;
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
        var existingProgress = cnn.Query<JiraEpicProgress>("select * from JiraEpicProgress where CreatedOn=@CreatedOn", new { CreatedOn = createdOn });
        log.Info($"Pulled {existingProgress.Count()} JiraEpicProgress from database OK.");

        if (existingProgress.Count() > 0)
        {
            var graph = ToGraph(existingProgress);
            string json = JsonConvert.SerializeObject(graph);
            return Message(req, json);
        }
    }

    string serviceUrl = ConfigurationManager.AppSettings["Jira.ServiceUrl"];
    string username = ConfigurationManager.AppSettings["Jira.Username"];
    string password = ConfigurationManager.AppSettings["Jira.Password"];
    string jql = ConfigurationManager.AppSettings["Jira.Jql.Epics"];

    // Get Epics linked to teams based on a status
    var epics = JiraEx.Get<IEnumerable<Issue>>(serviceUrl, $"/rest/api/2/search?jql={jql}&fields=summary,project,resolution,status,customfield_12000&maxResults=250", username, password, "issues")
        .ToList();

    log.Info($"Found {epics.Count()} epics");

    var epicProgress = new List<JiraEpicProgress>();
    foreach (var epic in epics)
    {
        var issues = JiraEx.Get<IEnumerable<Issue>>(serviceUrl, $"/rest/agile/1.0/epic/{epic.Key}/issue?fields=project,resolution,status,customfield_10004&maxResults=250", username, password, "issues")
            .ToList();
        var statusCounts = issues.GroupBy(i => i.Status)
            .Select(group => new
            {
                Status = group.Key,
                Count = group.Count()
            });
        var points = issues.GroupBy(i => i.Status)
            .Select(group => new
            {
                Status = group.Key,
                StoryPoints = group.Sum(i => i.StoryPoints)
            });

        var progress = new JiraEpicProgress
        {
            JiraId = epic.Key,
            CreatedOn = createdOn,
            EpicName = epic.Fields.Summary,
            TicketsDone = statusCounts.FirstOrDefault(v => v.Status == "Done")?.Count ?? 0,
            TicketsInDev = statusCounts.FirstOrDefault(v => v.Status == "InDev")?.Count ?? 0,
            TicketsInTest = statusCounts.FirstOrDefault(v => v.Status == "InTest")?.Count ?? 0,
            TicketsTodo = statusCounts.FirstOrDefault(v => v.Status == "Todo")?.Count ?? 0,
            PointsDone = (int)(points.FirstOrDefault(v => v.Status == "Done")?.StoryPoints ?? 0),
            PointsTodo = (int)(points.Where(v => v.Status != "Done").Select(v => v.StoryPoints).Sum()),
            Team = epic.Team,
        };
        epicProgress.Add(progress);
        log.Info($"Found {issues.Count()} issues in Epic {epic.Key}. '{progress.EpicName}' done");
    }

    // Now that all the data has been retrieved pump it into the DB
    using (var cnn = new SqlConnection(cnnString))
    {
        cnn.Open();
        foreach (var progress in epicProgress)
        {
            cnn.Execute("insert JiraEpicProgress(EpicName, CreatedOn, TicketsDone, TicketsInDev, TicketsInTest, TicketsTodo, JiraId, Team, PointsDone, PointsTodo) values(@epicName, @createdOn, @TicketsDone, @TicketsInDev, @TicketsInTest, @TicketsTodo, @jiraId, @team, @PointsDone, @PointsTodo)",
                new
                {
                    progress.EpicName,
                    progress.CreatedOn,
                    progress.TicketsDone,
                    progress.TicketsInDev,
                    progress.TicketsInTest,
                    progress.TicketsTodo,
                    progress.JiraId,
                    progress.Team,
                    progress.PointsDone,
                    progress.PointsTodo
                }
            );
        }
        log.Info("Log added to database successfully!");
    }

    {
        var graph = ToGraph(epicProgress);
        string json = JsonConvert.SerializeObject(graph);
        return Message(req, json);
    }
}

static Graph ToGraph(IEnumerable<JiraEpicProgress> epicProgress)
{
    var graph = new Graph { Title = "Epic Progress", DataSequences = new List<GraphSequence>() };

    foreach (var epic in epicProgress)
    {
        var sequence = new GraphSequence { Title = epic.EpicName, DataPoints = new List<GraphPoint>() };
        sequence.DataPoints.Add(new GraphPoint { Title = "Resolved", Value = epic.TicketsDone });
        sequence.DataPoints.Add(new GraphPoint { Title = "In Progress", Value = epic.TicketsInDev });
        graph.DataSequences.Add(sequence);
    }

    return graph;
}

static HttpResponseMessage Message(HttpRequestMessage req, string message)
{
    var response = req.CreateResponse();
    response.Content = new StringContent(message);
    response.Content.Headers.ContentType = JsonMediaTypeFormatter.DefaultMediaType;
    return response;
}

sealed class Board
{
    public int Id { get; set; }
    public string Self { get; set; }
    public string Name { get; set; }
    public string Type { get; set; }
}

sealed class Epic
{
    public int Id { get; set; }
    public string Self { get; set; }
    public string Name { get; set; }
    public string Summary { get; set; }
    public bool Done { get; set; }
}

sealed class Issue
{
    public string Key { get; set; }

    public string Status => Fields?.Status?.Name;
    public int? StatusId => Fields?.Status?.Id;
    public string Project => Fields?.Project?.Key;
    public bool IsResolved => Fields?.Resolution?.IsResolved ?? false;
    public string Team => Fields?.customfield_12000?.Value;
    public decimal StoryPoints => Fields?.customfield_10004 ?? 0;

    public Fields Fields { get; set; }
}

sealed class Fields
{
    public string Summary { get; set; }
    public Status Status { get; set; }
    public Project Project { get; set; }
    public Resolution Resolution { get; set; }
    public Team customfield_12000 { get; set; }
    public decimal? customfield_10004 { get; set; } // Story Points
}

sealed class Team
{
    public string Value { get; set; }
}

sealed class Status
{
    static Dictionary<string, IEnumerable<string>> Lookup = new Dictionary<string, IEnumerable<string>>
    {
        { "Todo", new List<string> { "Open", "todo", "To do", "Ready for Dev", "on hold", "awaiting clarification", "detail", "draft", "in review", "in janison review", "waiting on client", "queued", "outline" } },
        { "InDev", new List<string> { "In Progress", "Dev in progress" } },
        { "InTest", new List<string> { "Testing", "Janison testing", "waiting for build", "waiting for testing build", "beta testing" } },
        { "Done", new List<string> { "Fixed", "Done", "Closed", "Won't do", "waiting for sit deploy", "resolved", "esa testing", "in esa review" } }
    };

    string _name;
    public string Name
    {
        get
        {
            string lookupName = Lookup.First(l => l.Value.Any(v => v.Equals(_name, StringComparison.OrdinalIgnoreCase))).Key;
            return lookupName;
        }
        set
        {
            _name = value;
        }
    }
    public int Id { get; set; }
}

sealed class Project
{
    public string Key { get; set; }
}

sealed class ColumnConfig
{
    public IEnumerable<Columns> Columns { get; set; }
}

sealed class Columns
{
    public string Name { get; set; }
    public IEnumerable<ColumnStatus> Statuses { get; set; }
}

sealed class ColumnStatus
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
sealed class Graph
{
    public string Title { get; set; }
    public IList<GraphSequence> DataSequences { get; set; }
}

sealed class GraphSequence
{
    public string Title { get; set; }
    public IList<GraphPoint> DataPoints { get; set; }
}

sealed class GraphPoint
{
    public String Title { get; set; }
    public decimal? Value { get; set; }
}

/* Database */

sealed class JiraEpicProgress
{
    public int Id { get; set; }
    public string EpicName { get; set; }
    public DateTime CreatedOn { get; set; }
    public int TicketsDone { get; set; }
    public int TicketsInDev { get; set; }
    public int TicketsInTest { get; set; }
    public int TicketsTodo { get; set; }
    public int PointsTodo { get; set; }
    public int PointsDone { get; set; }
    public string JiraId { get; set; }
    public string Team { get; set; }
} 