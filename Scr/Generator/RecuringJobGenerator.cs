﻿using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using IeuanWalker.Hangfire.RecurringJob.Generator.Helpers;
using IeuanWalker.Hangfire.RecurringJob.Generator.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace IeuanWalker.Hangfire.RecurringJob.Generator;

[Generator(LanguageNames.CSharp)]
public class RecuringJobGenerator : IIncrementalGenerator
{
	static string? assemblyName;
	const string fullAttribute = "IeuanWalker.Hangfire.RecurringJob.Attributes.RecurringJobAttribute";

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		IncrementalValueProvider<ImmutableArray<JobModel?>> provider = context.SyntaxProvider
						 .ForAttributeWithMetadataName(fullAttribute, Match, Transform)
						 .Where(static r => r is not null)
						 .Collect();

		context.RegisterSourceOutput(provider, Generate!);
	}

	static bool Match(SyntaxNode node, CancellationToken _)
	{
		return true;
	}

	static JobModel? Transform(GeneratorAttributeSyntaxContext context, CancellationToken _)
	{
		IEnumerable<SyntaxNode> ancestors = context.TargetNode.Ancestors();
		if (ancestors.FirstOrDefault(x => x.IsKind(SyntaxKind.CompilationUnit)) is not CompilationUnitSyntax compilationUnit)
		{
			return null;
		}

		if (compilationUnit.Members.FirstOrDefault(m => m.IsKind(SyntaxKind.NamespaceDeclaration) || m.IsKind(SyntaxKind.FileScopedNamespaceDeclaration)) is not BaseNamespaceDeclarationSyntax)
		{
			return null;
		}

		INamedTypeSymbol? markerAttribute = context.SemanticModel.Compilation.GetTypeByMetadataName(fullAttribute);
		if (markerAttribute is null)
		{
			return null;
		}

		AttributeData? attribute = context.Attributes.FirstOrDefault(a => a?.AttributeClass is not null && a.AttributeClass.Equals(markerAttribute, SymbolEqualityComparer.Default));

		if (attribute is null)
		{
			return null;
		}

		assemblyName = context.SemanticModel.Compilation.AssemblyName;
		string jobId = (string?)attribute.NamedArguments.FirstOrDefault(a => a.Key == "JobId").Value.Value ?? context.TargetSymbol.Name;
		string cron = (string)attribute.NamedArguments.FirstOrDefault(a => a.Key == "Cron").Value.Value!;
		string queue = (string)attribute.NamedArguments.FirstOrDefault(a => a.Key == "Queue").Value.Value!;
		string timeZone = (string)attribute.NamedArguments.FirstOrDefault(a => a.Key == "TimeZone").Value.Value!;

		return new JobModel(context.TargetSymbol.ToString(), jobId, cron, queue, timeZone);
	}

	static void Generate(SourceProductionContext context, ImmutableArray<JobModel> jobs)
	{
		if (!jobs.Any())
		{
			return;
		}

		StringBuilder sb = new();

		sb.Append("namespace ").Append(assemblyName).Append(@";

// <auto-generated/>

using Hangfire;
using Microsoft.Extensions.DependencyInjection;

public static class RecurringJobRegistrationExtensions
{
	public static IServiceCollection RegisterRecurringJobsFrom").Append(assemblyName?.Sanitize(string.Empty) ?? "Assembly").Append(@"(this IServiceCollection sc)
	{
");
		foreach (JobModel job in jobs.OrderBy(r => r!.JobId))
		{
			sb.Append("\t\tRecurringJob.AddOrUpdate<").Append(job.FullClassName).Append(">(\"").Append(job.JobId).Append("\"").Append(", x => x.Execute(), \"").Append(job.Cron).Append("\");").Append("\r\n");
		}
		sb.Append(@"
		return sc;
	}
}");

		Debug.WriteLine(sb.ToString());

		context.AddSource("RecurringJobRegistrationExtensions.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
	}
}