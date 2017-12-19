﻿using System;
using System.Diagnostics;
using Elasticsearch.Net;

namespace Nest
{
	[ContractJsonConverter(typeof(IndexNameJsonConverter))]
	[DebuggerDisplay("{DebugDisplay,nq}")]
	public class IndexName : IEquatable<IndexName>, IUrlParameter
	{
		private static int TypeHashCode { get; } = typeof(IndexName).GetHashCode();

		private static readonly char[] ClusterSeparator = {':'};
		internal string DebugDisplay => Type == null ? Name : $"{nameof(IndexName)} for typeof: {Type?.Name}";

		public string Cluster { get; }
		public string Name { get; }
		public Type Type { get; }

		private IndexName(string index, string cluster = null)
		{
			this.Name = index?.Trim();
			this.Cluster = cluster?.Trim();
		}
		private IndexName(Type type, string cluster = null)
		{
			this.Type = type;
			this.Cluster = cluster?.Trim();
		}
		private IndexName(string index, Type type, string cluster = null)
		{
			this.Name = index;
			this.Type = type;
			this.Cluster = cluster?.Trim();
		}

		public static IndexName From<T>() => typeof(T);
		public static IndexName From<T>(string clusterName) => From(typeof(T), clusterName);
		private static IndexName From(Type t, string clusterName) => new IndexName(t, clusterName);
		public static IndexName Rebuild(string index, Type t, string clusterName = null) => new IndexName(index, t, clusterName);

		public Indices And<T>() => new Indices(new[] { this, typeof(T) });
		public Indices And<T>(string clusterName) => new Indices(new[] { this, From(typeof(T), clusterName) });
		public Indices And(IndexName index) => new Indices(new[] { this, index });

		private static IndexName Parse(string indexName)
		{
			if (string.IsNullOrWhiteSpace(indexName)) return null;
			var tokens = indexName.Split(ClusterSeparator, 2, StringSplitOptions.RemoveEmptyEntries);
			return tokens.Length == 1
				? new IndexName(tokens[0])
				: new IndexName(tokens[1],tokens[0]);
		}

		public static implicit operator IndexName(string indexName) => Parse(indexName);
		public static implicit operator IndexName(Type type) => type == null ? null : new IndexName(type);

		bool IEquatable<IndexName>.Equals(IndexName other) => EqualsMarker(other);

		public override bool Equals(object obj)
		{
			var s = obj as string;
			if (!s.IsNullOrEmpty()) return this.EqualsString(s);
			var pp = obj as IndexName;
			return EqualsMarker(pp);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				var result = TypeHashCode;
				result = (result * 397) ^ (this.Name?.GetHashCode() ?? this.Type?.GetHashCode() ?? 0);
				result = (result * 397) ^ (this.Cluster?.GetHashCode() ?? 0);
				return result;
			}
		}

		public override string ToString()
		{
			if (!this.Name.IsNullOrEmpty())
				return PrefixClusterName(this.Name);
			return this.Type != null ? PrefixClusterName(this.Type.Name) : string.Empty;
		}
		private string PrefixClusterName(string name) => PrefixClusterName(this, name);
		private static string PrefixClusterName(IndexName i, string name) => i.Cluster.IsNullOrEmpty() ? name : $"{i.Cluster}:{name}";

		private bool EqualsString(string other)
		{
			return !other.IsNullOrEmpty() && other == PrefixClusterName(this.Name);
		}

		private bool EqualsMarker(IndexName other)
		{
			if (!this.Name.IsNullOrEmpty() && other != null && !other.Name.IsNullOrEmpty())
				return EqualsString(PrefixClusterName(other,other.Name));

			if (this.Type != null && other != null && other.Type != null)
				return this.GetHashCode() == other.GetHashCode();
			return false;
		}

		public string GetString(IConnectionConfigurationValues settings)
		{
			if (!(settings is IConnectionSettingsValues nestSettings))
				throw new Exception("Tried to pass index name on querysting but it could not be resolved because no nest settings are available");

			return nestSettings.Inferrer.IndexName(this);
		}

	}
}
