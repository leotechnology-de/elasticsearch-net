﻿using System.Threading.Tasks;
using Elastic.Xunit.XunitPlumbing;
using Nest;
using Tests.Domain;
using Tests.Framework;
using static Tests.Framework.UrlTester;

namespace Tests.Document.Multiple.MultiTermVectors
{
	public class MultiTermVectorsUrlTests : UrlTestsBase
	{
		[U] public override async Task Urls()
		{
			await POST("/_mtermvectors")
					.Fluent(c => c.MultiTermVectors())
					.Request(c => c.MultiTermVectors(new MultiTermVectorsRequest()))
					.FluentAsync(c => c.MultiTermVectorsAsync())
					.RequestAsync(c => c.MultiTermVectorsAsync(new MultiTermVectorsRequest()))
				;

			await POST("/project/_mtermvectors")
					.Fluent(c => c.MultiTermVectors(m => m.Index<Project>()))
					.Request(c => c.MultiTermVectors(new MultiTermVectorsRequest(typeof(Project))))
					.FluentAsync(c => c.MultiTermVectorsAsync(m => m.Index<Project>()))
					.RequestAsync(c => c.MultiTermVectorsAsync(new MultiTermVectorsRequest(typeof(Project))))
				;

			await POST("/project/doc/_mtermvectors")
					.Fluent(c => c.MultiTermVectors(m => m.Index<Project>().Type<Project>()))
					.Request(c => c.MultiTermVectors(new MultiTermVectorsRequest(typeof(Project), typeof(Project))))
					.FluentAsync(c => c.MultiTermVectorsAsync(m => m.Index<Project>().Type<Project>()))
					.RequestAsync(c => c.MultiTermVectorsAsync(new MultiTermVectorsRequest(typeof(Project), typeof(Project))))
				;
		}
	}
}
