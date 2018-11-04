﻿using System.Threading.Tasks;
using Elastic.Xunit.XunitPlumbing;
using Nest;
using Tests.Domain;
using Tests.Framework;
using static Tests.Framework.UrlTester;

namespace Tests.Search.Explain
{
	public class ExplainUrlTests
	{
		[U] public async Task Urls()
		{
			var project = new Project { Name = "NEST" };

			await POST("/project/project/NEST/_explain")
					.Fluent(c => c.Explain<Project>("NEST", e => e.Query(q => q.MatchAll())))
					.Request(c => c.Explain(new ExplainRequest<Project>("project", "project", "NEST") { }))
					.FluentAsync(c => c.ExplainAsync<Project>(project, e => e.Query(q => q.MatchAll())))
					.RequestAsync(c => c.ExplainAsync(new ExplainRequest<Project>("NEST")))
				;
		}
	}
}
