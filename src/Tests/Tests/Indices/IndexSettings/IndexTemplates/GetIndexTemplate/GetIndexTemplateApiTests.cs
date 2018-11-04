﻿using System;
using Elasticsearch.Net;
using FluentAssertions;
using Nest;
using Tests.Core.Extensions;
using Tests.Core.ManagedElasticsearch.Clusters;
using Tests.Framework;
using Tests.Framework.Integration;

namespace Tests.Indices.IndexSettings.IndexTemplates.GetIndexTemplate
{
	public class GetIndexTemplateApiTests
		: ApiIntegrationTestBase<WritableCluster, IGetIndexTemplateResponse, IGetIndexTemplateRequest, GetIndexTemplateDescriptor,
			GetIndexTemplateRequest>
	{
		public GetIndexTemplateApiTests(WritableCluster cluster, EndpointUsage usage) : base(cluster, usage) { }

		protected override bool ExpectIsValid => true;

		protected override object ExpectJson => null;

		protected override int ExpectStatusCode => 200;

		protected override Func<GetIndexTemplateDescriptor, IGetIndexTemplateRequest> Fluent => d => d
			.Name(CallIsolatedValue);

		protected override HttpMethod HttpMethod => HttpMethod.GET;

		protected override GetIndexTemplateRequest Initializer => new GetIndexTemplateRequest(CallIsolatedValue);
		protected override string UrlPath => $"/_template/{CallIsolatedValue}";

		protected override LazyResponses ClientUsage() => Calls(
			(client, f) => client.GetIndexTemplate(f),
			(client, f) => client.GetIndexTemplateAsync(f),
			(client, r) => client.GetIndexTemplate(r),
			(client, r) => client.GetIndexTemplateAsync(r)
		);

		protected override void IntegrationSetup(IElasticClient client, CallUniqueValues values)
		{
			foreach (var callUniqueValue in values)
			{
#pragma warning disable 618
				var putTemplateResponse = client.PutIndexTemplate(callUniqueValue.Value, d =>
					d.Template("nestx-*")
						.Settings(s => s.NumberOfShards(2))
						.Version(1)
				);
#pragma warning restore 618

				if (!putTemplateResponse.IsValid)
					throw new Exception($"Problem putting index template for integration test: {putTemplateResponse.DebugInformation}");
			}
		}

		protected override void ExpectResponse(IGetIndexTemplateResponse response)
		{
			response.ShouldBeValid();

			response.TemplateMappings.Should().NotBeNull();
			response.TemplateMappings.Should().HaveCount(1);

			var responseTemplateMapping = response.TemplateMappings[CallIsolatedValue];

#pragma warning disable 618
			responseTemplateMapping.Template.Should().NotBeNull();
			responseTemplateMapping.Template.Should().Be("nestx-*");
#pragma warning restore 618

			responseTemplateMapping.Version.Should().Be(1);

			responseTemplateMapping.Settings.Should().NotBeNull();
			responseTemplateMapping.Settings.NumberOfShards.Should().Be(2);
		}
	}
}
