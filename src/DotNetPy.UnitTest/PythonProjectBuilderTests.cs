using DotNetPy.Uv;

namespace DotNetPy.UnitTest;

[TestClass]
public sealed class PythonProjectBuilderTests
{
    [TestMethod]
    public void CreateBuilder_ReturnsNewInstance()
    {
        var builder = PythonProject.CreateBuilder();
        Assert.IsNotNull(builder);
    }

    [TestMethod]
    public void WithProjectName_SetsProjectName()
    {
        var builder = new PythonProjectBuilder()
            .WithProjectName("test-project");

        var toml = builder.GeneratePyProjectToml();

        Assert.Contains("name = \"test-project\"", toml);
    }

    [TestMethod]
    public void WithVersion_SetsVersion()
    {
        var builder = new PythonProjectBuilder()
            .WithVersion("2.0.0");

        var toml = builder.GeneratePyProjectToml();

        Assert.Contains("version = \"2.0.0\"", toml);
    }

    [TestMethod]
    public void WithDescription_SetsDescription()
    {
        var builder = new PythonProjectBuilder()
            .WithDescription("A test project");

        var toml = builder.GeneratePyProjectToml();

        Assert.Contains("description = \"A test project\"", toml);
    }

    [TestMethod]
    public void WithPythonVersion_SetsPythonVersionConstraint()
    {
        var builder = new PythonProjectBuilder()
            .WithPythonVersion(">=3.10");

        var toml = builder.GeneratePyProjectToml();

        Assert.Contains("requires-python = \">=3.10\"", toml);
    }

    [TestMethod]
    public void WithPythonVersion_NormalizesPlainVersion()
    {
        var builder = new PythonProjectBuilder()
            .WithPythonVersion("3.11");

        var toml = builder.GeneratePyProjectToml();

        Assert.Contains("requires-python = \">=3.11\"", toml);
    }

    [TestMethod]
    public void AddDependency_AddsSingleDependency()
    {
        var builder = new PythonProjectBuilder()
            .AddDependency("numpy", ">=1.24.0");

        var toml = builder.GeneratePyProjectToml();

        Assert.Contains("dependencies = [", toml);
        Assert.Contains("\"numpy>=1.24.0\"", toml);
    }

    [TestMethod]
    public void AddDependency_WithoutVersion_AddsPackageOnly()
    {
        var builder = new PythonProjectBuilder()
            .AddDependency("requests");

        var toml = builder.GeneratePyProjectToml();

        Assert.Contains("\"requests\"", toml);
    }

    [TestMethod]
    public void AddDependencies_AddsMultipleDependencies()
    {
        var builder = new PythonProjectBuilder()
            .AddDependencies("numpy>=1.24.0", "pandas>=2.0.0", "scikit-learn>=1.3.0");

        var toml = builder.GeneratePyProjectToml();

        Assert.Contains("\"numpy>=1.24.0\"", toml);
        Assert.Contains("\"pandas>=2.0.0\"", toml);
        Assert.Contains("\"scikit-learn>=1.3.0\"", toml);
    }

    [TestMethod]
    public void AddDevDependency_AddsToOptionalDependencies()
    {
        var builder = new PythonProjectBuilder()
            .AddDevDependency("pytest", ">=7.0.0");

        var toml = builder.GeneratePyProjectToml();

        Assert.Contains("[project.optional-dependencies]", toml);
        Assert.Contains("dev = [", toml);
        Assert.Contains("\"pytest>=7.0.0\"", toml);
    }

    [TestMethod]
    public void GeneratePyProjectToml_IncludesUvSection()
    {
        var builder = new PythonProjectBuilder();

        var toml = builder.GeneratePyProjectToml();

        Assert.Contains("[tool.uv]", toml);
        Assert.Contains("managed = true", toml);
    }

    [TestMethod]
    public void WithUvSetting_AddsCustomSetting()
    {
        var builder = new PythonProjectBuilder()
            .WithUvSetting("python-preference", "only-managed");

        var toml = builder.GeneratePyProjectToml();

        Assert.Contains("python-preference = \"only-managed\"", toml);
    }

    [TestMethod]
    public void Build_ReturnsPythonProject()
    {
        var project = new PythonProjectBuilder()
            .WithProjectName("test")
            .WithVersion("1.0.0")
            .AddDependency("numpy")
            .Build();

        Assert.IsNotNull(project);
        Assert.AreEqual("test", project.ProjectName);
        Assert.AreEqual("1.0.0", project.Version);
        Assert.HasCount(1, project.Dependencies);
    }

    [TestMethod]
    public void CompleteExample_GeneratesValidToml()
    {
        var builder = new PythonProjectBuilder()
            .WithProjectName("my-ml-project")
            .WithVersion("0.1.0")
            .WithDescription("Machine learning project")
            .WithPythonVersion(">=3.10,<4.0")
            .AddDependency("numpy", ">=1.24.0")
            .AddDependency("pandas", ">=2.0.0")
            .AddDependency("scikit-learn", ">=1.3.0")
            .AddDevDependency("pytest", ">=7.0.0")
            .AddDevDependency("black");

        var toml = builder.GeneratePyProjectToml();

        Console.WriteLine(toml);

        // Verify structure
        Assert.Contains("[project]", toml);
        Assert.Contains("name = \"my-ml-project\"", toml);
        Assert.Contains("version = \"0.1.0\"", toml);
        Assert.Contains("description = \"Machine learning project\"", toml);
        Assert.Contains("requires-python = \">=3.10,<4.0\"", toml);
        Assert.Contains("dependencies = [", toml);
        Assert.Contains("[project.optional-dependencies]", toml);
        Assert.Contains("[tool.uv]", toml);
    }
}
