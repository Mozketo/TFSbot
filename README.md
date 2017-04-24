# TFSbot - An Azure Functions for Slack

Add a TFSbot to your Slack channels. Built for some specific use-cases so you may wish to fork and change for your needs.

## Azure

1. Provision an Azure Function App (I set-up as a consumption app, but this is up to you),
2. In the Function App set-up as Continuous Integration and connect to GitHub,
3. Ensure that you select the correct GitHub project and branch,
4. Configure the App Settings for the Function,
* If you wish you can disable PHP for this App.
* `Tfs.Url` - Example: https://tfs.domain.com:443/tfs/collection.
* `Tfs.Domain` - May be required for authentication if TFS is running in a Domain.
* `Tfs.Username` - Username to connect to TFS. Sorry, I've not looked at authentication via oAuth.
* `Tfs.Password` - Password to connect to TFS.
* `Jira.IgnoreProjects` - Exmaple: Ignore JIRA projects `ops qa`.
* `Slack.Token` - A token that Slack Outgoing WebHook provides.

## Slack

1. Create an Outgoing WebHook,
2. Trigger word: tfsbot,
3. URL: Use the Azure Functions URL like `https://<app-name>.azurewebsites.net/api/TfsBot?code=<code>` as displayed in the Azure Functions portal,
4. Token: You'll need this in the Azure Functions App Settings,
5. Descriptive label: TFSbot
6. Customise name: TFS

## Usage

Once everything is wired up in Slack and Azure you can use the the bot like this:

`tfsbot not-reviewed yyyy-MM-dd` - Changesets not peer-reviewed. 
`tfsbot missing-jira yyyy-MM-dd` - Changesets missing Jira IDs. 
`tfsbot tickets yyyy-MM-dd` - Changeset to Jira activity. 
`tfsbot search <term>` - Search 30 days of history. 
`tfsbot search-user <username>` - Find 30 days of changes by committer. 
`tfsbot merge /source /destination [username]` - List of merge candidates (changesets) between the source and destination. 
`tfsbot stats <date> [username1] [username2] ... [username3]` - Code review stats per username.
