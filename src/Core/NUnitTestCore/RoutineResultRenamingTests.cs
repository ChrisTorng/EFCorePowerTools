using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.SqlServer.Design.Internal;
using Microsoft.Extensions.DependencyInjection;
using RevEng.Common;
using RevEng.Core;
using RevEng.Core.Abstractions;
using RevEng.Core.Abstractions.Metadata;
using RevEng.Core.Routines;
using RevEng.Core.Routines.Extensions;
using Xunit;

namespace UnitTests;

public class RoutineResultRenamingTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ExplicitResultRenamingPreservesColumnMapping(bool procedure, bool pascalCase)
    {
        Routine routine = procedure ? new Procedure() : new Function();
        routine.Name = "testtext";
        routine.Schema = "public";
        routine.Results = new List<List<ModuleResultElement>>
        {
            new()
            {
                new() { Name = "resultcolumn", StoreType = "int", Ordinal = 0 },
                new() { Name = "unmapped_column", StoreType = "int", Ordinal = 1 },
            },
            new()
            {
                new() { Name = "resultcolumn", StoreType = "int", Ordinal = 0 },
                new() { Name = "keywordcolumn", StoreType = "int", Ordinal = 1 },
            },
        };
        var renamers = new List<Schema>
        {
            new()
            {
                SchemaName = "public",
                Tables = new List<TableRenamer>
                {
                    new()
                    {
                        Name = "testtext",
                        NewName = "TestText",
                        Columns = new List<ColumnNamer>
                        {
                            new() { Name = "resultcolumn", NewName = "ResultColumn" },
                            new() { Name = "keywordcolumn", NewName = "class" },
                        },
                    },
                },
            },
        };

        var apply = typeof(ReverseEngineerScaffolder).GetMethod("ApplyRenamers", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(apply);
        apply.Invoke(null, new object[] { new[] { routine }, renamers });
        Assert.Equal("TestText", routine.NewName);
        var services = new ServiceCollection();
        services.AddEntityFrameworkDesignTimeServices();
#pragma warning disable EF1001
        new SqlServerDesignTimeServices().ConfigureDesignTimeServices(services);
#pragma warning restore EF1001
        services.AddSingleton<IClrTypeMapper, SqlServerClrTypeMapper>();
        using var provider = services.BuildServiceProvider();
        var scaffolder = new Scaffolder(provider.GetRequiredService<ICSharpHelper>(), provider.GetRequiredService<IClrTypeMapper>());
        var options = new ModuleScaffolderOptions
        {
            ModelNamespace = "TestNamespace",
            UsePascalIdentifiers = pascalCase,
        };

        var first = scaffolder.WriteResultClass(routine.Results[0], options, "TestTextResult1", "public");
        var second = scaffolder.WriteResultClass(routine.Results[1], options, "TestTextResult2", "public");
        foreach (var code in new[] { first, second })
        {
            Assert.Contains("[Column(\"resultcolumn\")]", code);
            Assert.Contains("int ResultColumn { get; set; }", code);
            Assert.DoesNotContain("int resultcolumn { get; set; }", code);
        }

        Assert.Contains(pascalCase ? "int UnmappedColumn {" : "int unmapped_column {", first);
        Assert.Contains("[Column(\"keywordcolumn\")]", second);
        Assert.Contains("int @class {", second);
        Assert.Equal("resultcolumn", routine.Results[0][0].Name);
    }
}
