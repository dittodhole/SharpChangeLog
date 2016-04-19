using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text.RegularExpressions;
using MoreLinq;
using NDesk.Options.Extensions;
using Redmine.Net.Api;
using Redmine.Net.Api.Types;
using SharpSvn;

// ReSharper disable ExceptionNotDocumentedOptional
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable ExceptionNotDocumented

namespace SharpChangeLog
{
  public static class Program
  {
    private static void Main(string[] args)
    {
      // TODO this code is prone to invalid inputs... idc :bikini:

      var requiredValuesOptionSet = new RequiredValuesOptionSet();
      var repository = requiredValuesOptionSet.AddRequiredVariable<string>("repository",
                                                                           "Specifies the URI to the SVN repository");
      var trunk = requiredValuesOptionSet.AddRequiredVariable<string>("trunk",
                                                                      "Specifies the relative path to the trunk, which is diffed against the <branch> parameter (usually trunk)");
      var branch = requiredValuesOptionSet.AddRequiredVariable<string>("branch",
                                                                       "Specifies the relative path to the branch, whose initial revision is used as the offset for the change log");
      var redmineHost = requiredValuesOptionSet.AddRequiredVariable<string>("redmineHost",
                                                                            "Specifies the host, which serves the Redmine instance");
      var redmineApiKey = requiredValuesOptionSet.AddRequiredVariable<string>("redmineApiKey",
                                                                              "Specifies the api key that is used for communication with the Redmine instance");
      var ignoreBranchIssues = requiredValuesOptionSet.AddVariable<bool>("ignoreBranchIssues",
                                                                         "Specifies whether issues, comitted to the branch, should be ignored or not. Default is false");
      var consoleManager = new ConsoleManager("ChangeLog Generator",
                                              requiredValuesOptionSet);
      if (!consoleManager.TryParseOrShowHelp(Console.Out,
                                             args))
      {
        return;
      }

      Console.WriteLine("Getting the initial revision of {0}",
                        branch.Value);
      var initialLogItem = Program.GetInitialLogItem(repository,
                                                     branch);
      if (initialLogItem == null)
      {
        Console.WriteLine("Could not get the initial revision of {0}",
                          branch.Value);
        return;
      }

      long[] branchIssueIds;
      if (ignoreBranchIssues)
      {
        Console.WriteLine("Getting log for {0} since {1} ({2})",
                          branch.Value,
                          initialLogItem.Revision,
                          initialLogItem.Time);
        var branchLogItems = Program.GetLogSince(repository,
                                                 branch,
                                                 initialLogItem.Revision);
        branchIssueIds = branchLogItems.SelectMany(arg => arg.GetIssueIds())
                                       .Distinct()
                                       .ToArray();
      }
      else
      {
        branchIssueIds = new long[0];
      }

      var revision = initialLogItem.ChangedPaths.Max(arg => arg.CopyFromRevision);
      Console.WriteLine("Getting log for {0} since {1}",
                        trunk.Value,
                        revision);
      var logItems = Program.GetLogSince(repository,
                                         trunk,
                                         revision);

      var complexIssueIds = logItems.SelectMany(arg => arg.GetIssueIds()
                                                          .Except(branchIssueIds)
                                                          .Select(issueId => new
                                                                             {
                                                                               LogItem = arg,
                                                                               IssueId = issueId
                                                                             }))
                                    .OrderBy(arg => arg.IssueId)
                                    .GroupBy(arg => arg.IssueId)
                                    .ToDictionary(arg => arg.Key);

      Console.WriteLine("Getting the issues for {0} since {1} ({3}) from {2}",
                        trunk.Value,
                        revision,
                        redmineHost.Value,
                        initialLogItem.Time);
      var issues = Program.GetIssues(redmineHost,
                                     redmineApiKey,
                                     complexIssueIds.Keys);

      issues.ForEach(issue =>
                     {
                       Console.WriteLine("#{0}: {1} (assigned to: {2}, commits by: {3})",
                                         issue.Id,
                                         issue.Subject,
                                         issue.AssignedTo?.Name ?? "none",
                                         string.Join(", ",
                                                     complexIssueIds[issue.Id].Select(arg => arg.LogItem)
                                                                              .Select(arg => arg.Author)
                                                                              .Distinct()));
                     });
    }

    public static SvnLogEventArgs GetInitialLogItem(string repositoryUri,
                                                    string target)
    {
      var svnLogArgs = new SvnLogArgs
                       {
                         StrictNodeHistory = true,
                         BaseUri = new Uri(repositoryUri),
                         Start = SvnRevision.Zero,
                         End = SvnRevision.Head,
                         Limit = 1
                       };

      Collection<SvnLogEventArgs> logItems;
      using (var svnClient = new SvnClient())
      {
        if (!svnClient.GetLog(new Uri(svnLogArgs.BaseUri,
                                      target),
                              svnLogArgs,
                              out logItems))
        {
          logItems = new Collection<SvnLogEventArgs>();
        }
      }

      var logItem = logItems.SingleOrDefault();

      return logItem;
    }

    public static ICollection<SvnLogEventArgs> GetLogSince(string repositoryUri,
                                                           string target,
                                                           long revision)
    {
      var svnLogArgs = new SvnLogArgs
                       {
                         StrictNodeHistory = true,
                         End = revision,
                         BaseUri = new Uri(repositoryUri)
                       };

      Collection<SvnLogEventArgs> logItems;
      using (var svnClient = new SvnClient())
      {
        if (!svnClient.GetLog(new Uri(svnLogArgs.BaseUri,
                                      target),
                              svnLogArgs,
                              out logItems))
        {
          logItems = new Collection<SvnLogEventArgs>();
        }
      }

      return logItems;
    }

    public static IEnumerable<Issue> GetIssues(string redmineHost,
                                               string redmineApiKey,
                                               IEnumerable<long> issueIds)
    {
      var redmineManager = new RedmineManager(redmineHost,
                                              redmineApiKey,
                                              MimeFormat.json);
      var result = issueIds.AsParallel() // we are doing a .AsParallel here, as the Redmine API is very slow :confused:
                           .Select(arg =>
                                   {
                                     var issueId = arg.ToString();
                                     var issue = redmineManager.GetObject<Issue>(issueId,
                                                                                 new NameValueCollection());

                                     return issue;
                                   })
                           .ToArray();

      return result;
    }

    public static IEnumerable<long> GetIssueIds(this SvnLogEventArgs svnLogEventArgs)
    {
      var result = Regex.Matches(svnLogEventArgs.LogMessage,
                                 @"#(\d+)",
                                 RegexOptions.Compiled)
                        .Cast<Match>()
                        .Select(match => long.Parse(match.Groups.Cast<System.Text.RegularExpressions.Group>()
                                                         .ElementAt(1)
                                                         .Value))
                        .ToArray();

      return result;
    }
  }
}