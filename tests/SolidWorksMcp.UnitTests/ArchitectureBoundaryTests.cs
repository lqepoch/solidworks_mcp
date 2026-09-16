using System.Xml.Linq;

namespace SolidWorksMcp.UnitTests;

public sealed class ArchitectureBoundaryTests
{
    private static readonly string[] sourceArray = ["src", "providers", "testing", "tests"];

    // This test guards the vendor boundary at project-file level before runtime architecture tests exist.
    // 此测试先在项目文件层面守住厂商依赖边界，后续再补充运行时架构检查。
    [Fact]
    public void VendorInteropReferencesAreRestrictedToSolidWorksProvider()
    {
        string repositoryRoot = FindRepositoryRoot();
        // Scan only product/test roots; references/ contains upstream projects and is intentionally not product code.
        // 只扫描产品和测试根目录；references/ 是上游研究仓库，不属于产品代码。
        var projectFiles = sourceArray.SelectMany(directory => Directory.EnumerateFiles(Path.Combine(repositoryRoot, directory), "*.csproj", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        foreach (string? projectFile in projectFiles)
        {
            string projectName = Path.GetFileNameWithoutExtension(projectFile);
            if (projectName.Equals("SolidWorksMcp.Provider.SolidWorks", StringComparison.Ordinal))
            {
                continue;
            }

            var document = XDocument.Load(projectFile);
            string? forbidden = document.Descendants()
                .Where(element => element.Name.LocalName is "PackageReference" or "Reference")
                .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
                .FirstOrDefault(include => include.Contains("SolidWorks.Interop", StringComparison.OrdinalIgnoreCase));

            Assert.True(forbidden is null, $"{projectName} references forbidden vendor assembly '{forbidden}'.");
        }
    }

    [Fact]
    public void ProductProjectsDoNotReferenceTestingProjects()
    {
        string repositoryRoot = FindRepositoryRoot();
        var productProjects = Directory.EnumerateFiles(Path.Combine(repositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(repositoryRoot, "providers"), "*.csproj", SearchOption.AllDirectories));

        foreach (string? projectFile in productProjects)
        {
            var references = XDocument.Load(projectFile).Descendants("ProjectReference")
                .Select(element => (string?)element.Attribute("Include") ?? string.Empty);
            Assert.DoesNotContain(references, reference => reference.Contains("testing", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(references, reference => reference.Contains("tests", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SolidWorksMcp.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate SolidWorksMcp.slnx from the test output directory.");
    }
}
