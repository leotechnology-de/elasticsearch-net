using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Elasticsearch.Net;
using FluentAssertions;
using Nest;
using Tests.Framework.MockResponses;
using HttpMethod = Elasticsearch.Net.HttpMethod;

namespace Tests.Framework
{
	public class VirtualClusterConnection : InMemoryConnection
	{
		private static readonly object _lock = new object();

		private static byte[] DefaultResponseBytes;

		private VirtualCluster _cluster;
		private readonly TestableDateTimeProvider _dateTimeProvider;
		private IDictionary<int, State> Calls = new Dictionary<int, State> { };

		public VirtualClusterConnection(VirtualCluster cluster, TestableDateTimeProvider dateTimeProvider)
		{
			UpdateCluster(cluster);
			_dateTimeProvider = dateTimeProvider;
		}

		private static object DefaultResponse
		{
			get
			{
				var response = new
				{
					name = "Razor Fist",
					cluster_name = "elasticsearch-test-cluster",
					version = new
					{
						number = "2.0.0",
						build_hash = "af1dc6d8099487755c3143c931665b709de3c764",
						build_timestamp = "2015-07-07T11:28:47Z",
						build_snapshot = true,
						lucene_version = "5.2.1"
					},
					tagline = "You Know, for Search"
				};
				return response;
			}
		}

		public void UpdateCluster(VirtualCluster cluster)
		{
			if (cluster == null) return;

			lock (_lock)
			{
				_cluster = cluster;
				Calls = cluster.Nodes.ToDictionary(n => n.Uri.Port, v => new State());
			}
		}

		public bool IsSniffRequest(RequestData requestData) => requestData.Path.StartsWith("_nodes/http,settings", StringComparison.Ordinal);

		public bool IsPingRequest(RequestData requestData) => requestData.Path == "/" && requestData.Method == HttpMethod.HEAD;

		public override ElasticsearchResponse<TReturn> Request<TReturn>(RequestData requestData)
		{
			Calls.Should().ContainKey(requestData.Uri.Port);
			try
			{
				var state = Calls[requestData.Uri.Port];
				if (IsSniffRequest(requestData))
				{
					var sniffed = Interlocked.Increment(ref state.Sniffed);
					return HandleRules<TReturn, ISniffRule>(
						requestData,
						_cluster.SniffingRules,
						requestData.RequestTimeout,
						(r) => UpdateCluster(r.NewClusterState),
						(r) => SniffResponseBytes.Create(_cluster.Nodes, _cluster.PublishAddressOverride, _cluster.SniffShouldReturnFqnd)
					);
				}
				if (IsPingRequest(requestData))
				{
					var pinged = Interlocked.Increment(ref state.Pinged);
					return HandleRules<TReturn, IRule>(
						requestData,
						_cluster.PingingRules,
						requestData.PingTimeout,
						(r) => { },
						(r) => null //HEAD request
					);
				}
				var called = Interlocked.Increment(ref state.Called);
				return HandleRules<TReturn, IClientCallRule>(
					requestData,
					_cluster.ClientCallRules,
					requestData.RequestTimeout,
					(r) => { },
					CallResponse
				);
			}
#if DOTNETCORE
			catch (HttpRequestException e)
#else
			catch (WebException e)
#endif
			{
				var builder = new ResponseBuilder<TReturn>(requestData);
				builder.Exception = e;
				return builder.ToResponse();
			}
		}

		private ElasticsearchResponse<TReturn> HandleRules<TReturn, TRule>(
			RequestData requestData,
			IEnumerable<TRule> rules,
			TimeSpan timeout,
			Action<TRule> beforeReturn,
			Func<TRule, byte[]> successResponse
		) where TReturn : class where TRule : IRule
		{
			requestData.MadeItToResponse = true;

			var state = Calls[requestData.Uri.Port];
			foreach (var rule in rules.Where(s => s.OnPort.HasValue))
			{
				var always = rule.Times.Match(t => true, t => false);
				var times = rule.Times.Match(t => -1, t => t);
				if (rule.OnPort.Value == requestData.Uri.Port)
				{
					if (always)
						return Always<TReturn, TRule>(requestData, timeout, beforeReturn, successResponse, rule);

					return Sometimes<TReturn, TRule>(requestData, timeout, beforeReturn, successResponse, state, rule, times);
				}
			}
			foreach (var rule in rules.Where(s => !s.OnPort.HasValue))
			{
				var always = rule.Times.Match(t => true, t => false);
				var times = rule.Times.Match(t => -1, t => t);
				if (always)
					return Always<TReturn, TRule>(requestData, timeout, beforeReturn, successResponse, rule);

				return Sometimes<TReturn, TRule>(requestData, timeout, beforeReturn, successResponse, state, rule, times);
			}
			return ReturnConnectionStatus<TReturn>(requestData, successResponse(default(TRule)));
		}

		private ElasticsearchResponse<TReturn> Always<TReturn, TRule>(RequestData requestData, TimeSpan timeout, Action<TRule> beforeReturn,
			Func<TRule, byte[]> successResponse, TRule rule
		)
			where TReturn : class
			where TRule : IRule
		{
			if (rule.Takes.HasValue)
			{
				var time = timeout < rule.Takes.Value ? timeout : rule.Takes.Value;
				_dateTimeProvider.ChangeTime(d => d.Add(time));
				if (rule.Takes.Value > requestData.RequestTimeout)
#if DOTNETCORE
					throw new HttpRequestException(
						$"Request timed out after {time} : call configured to take {rule.Takes.Value} while requestTimeout was: {timeout}");
#else
					throw new WebException($"Request timed out after {time} : call configured to take {rule.Takes.Value} while requestTimeout was: {timeout}");
#endif
			}

			return rule.Succeeds
				? Success<TReturn, TRule>(requestData, beforeReturn, successResponse, rule)
				: Fail<TReturn, TRule>(requestData, rule);
		}

		private ElasticsearchResponse<TReturn> Sometimes<TReturn, TRule>(
			RequestData requestData, TimeSpan timeout, Action<TRule> beforeReturn, Func<TRule, byte[]> successResponse, State state, TRule rule,
			int times
		)
			where TReturn : class
			where TRule : IRule
		{
			if (rule.Takes.HasValue)
			{
				var time = timeout < rule.Takes.Value ? timeout : rule.Takes.Value;
				_dateTimeProvider.ChangeTime(d => d.Add(time));
				if (rule.Takes.Value > requestData.RequestTimeout)
#if DOTNETCORE
					throw new HttpRequestException(
						$"Request timed out after {time} : call configured to take {rule.Takes.Value} while requestTimeout was: {timeout}");
#else
					throw new WebException($"Request timed out after {time} : call configured to take {rule.Takes.Value} while requestTimeout was: {timeout}");
#endif
			}

			if (rule.Succeeds && times >= state.Successes)
				return Success<TReturn, TRule>(requestData, beforeReturn, successResponse, rule);
			else if (rule.Succeeds) return Fail<TReturn, TRule>(requestData, rule);

			if (!rule.Succeeds && times >= state.Failures)
				return Fail<TReturn, TRule>(requestData, rule);

			return Success<TReturn, TRule>(requestData, beforeReturn, successResponse, rule);
		}

		private ElasticsearchResponse<TReturn> Fail<TReturn, TRule>(RequestData requestData, TRule rule, Union<Exception, int> returnOverride = null)
			where TReturn : class
			where TRule : IRule
		{
			var state = Calls[requestData.Uri.Port];
			var failed = Interlocked.Increment(ref state.Failures);
			var ret = returnOverride ?? rule.Return;

			if (ret == null)
#if DOTNETCORE
				throw new HttpRequestException();
#else
				throw new WebException();
#endif
			return ret.Match(
				(e) => throw e,
				(statusCode) => ReturnConnectionStatus<TReturn>(requestData, CallResponse(rule),
					//make sure we never return a valid status code in Fail responses because of a bad rule.
					statusCode >= 200 && statusCode < 300 ? 502 : statusCode)
			);
		}

		private ElasticsearchResponse<TReturn> Success<TReturn, TRule>(RequestData requestData, Action<TRule> beforeReturn,
			Func<TRule, byte[]> successResponse, TRule rule
		)
			where TReturn : class
			where TRule : IRule
		{
			var state = Calls[requestData.Uri.Port];
			var succeeded = Interlocked.Increment(ref state.Successes);
			beforeReturn?.Invoke(rule);
			return ReturnConnectionStatus<TReturn>(requestData, successResponse(rule));
		}

		private byte[] CallResponse<TRule>(TRule rule)
			where TRule : IRule
		{
			if (rule?.ReturnResponse != null)
				return rule.ReturnResponse;

			if (DefaultResponseBytes != null) return DefaultResponseBytes;

			var response = DefaultResponse;
			using (var ms = new MemoryStream())
			{
				new ElasticsearchDefaultSerializer().Serialize(response, ms);
				DefaultResponseBytes = ms.ToArray();
			}
			return DefaultResponseBytes;
		}

		public override Task<ElasticsearchResponse<TReturn>> RequestAsync<TReturn>(RequestData requestData, CancellationToken cancellationToken) =>
			Task.FromResult(Request<TReturn>(requestData));

		private class State
		{
			public int Called = 0;
			public int Failures = 0;
			public int Pinged = 0;
			public int Sniffed = 0;
			public int Successes = 0;
		}
	}
}
