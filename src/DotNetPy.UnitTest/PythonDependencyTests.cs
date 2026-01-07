using DotNetPy.Uv;

namespace DotNetPy.UnitTest;

[TestClass]
public sealed class PythonDependencyTests
{
    [TestMethod]
    public void Constructor_WithNameOnly_CreatesValidDependency()
    {
        var dep = new PythonDependency("numpy");

        Assert.AreEqual("numpy", dep.Name);
        Assert.IsNull(dep.VersionConstraint);
        Assert.IsEmpty(dep.Extras);
    }

    [TestMethod]
    public void Constructor_WithVersion_CreatesValidDependency()
    {
        var dep = new PythonDependency("numpy", ">=1.24.0");

        Assert.AreEqual("numpy", dep.Name);
        Assert.AreEqual(">=1.24.0", dep.VersionConstraint);
    }

    [TestMethod]
    public void Constructor_WithExtras_CreatesValidDependency()
    {
        var dep = new PythonDependency("requests", ">=2.28.0", ["security", "socks"]);

        Assert.AreEqual("requests", dep.Name);
        Assert.AreEqual(">=2.28.0", dep.VersionConstraint);
        Assert.HasCount(2, dep.Extras);
        Assert.IsTrue(dep.Extras.Contains("security"));
        Assert.IsTrue(dep.Extras.Contains("socks"));
    }

    [TestMethod]
    public void Constructor_WithEmptyName_ThrowsException()
    {
        Assert.ThrowsExactly<ArgumentException>(() => _ = new PythonDependency(""));
    }

    [TestMethod]
    public void Constructor_WithWhitespaceName_ThrowsException()
    {
        Assert.ThrowsExactly<ArgumentException>(() => _ = new PythonDependency("   "));
    }

    [TestMethod]
    public void ToPep508String_NameOnly_ReturnsName()
    {
        var dep = new PythonDependency("numpy");

        Assert.AreEqual("numpy", dep.ToPep508String());
    }

    [TestMethod]
    public void ToPep508String_WithVersion_ReturnsNameAndVersion()
    {
        var dep = new PythonDependency("numpy", ">=1.24.0");

        Assert.AreEqual("numpy>=1.24.0", dep.ToPep508String());
    }

    [TestMethod]
    public void ToPep508String_WithExtras_ReturnsFormattedString()
    {
        var dep = new PythonDependency("requests", ">=2.28.0", ["security"]);

        Assert.AreEqual("requests[security]>=2.28.0", dep.ToPep508String());
    }

    [TestMethod]
    public void ToPep508String_WithMultipleExtras_ReturnsFormattedString()
    {
        var dep = new PythonDependency("uvicorn", null, ["standard"]);

        Assert.AreEqual("uvicorn[standard]", dep.ToPep508String());
    }

    [TestMethod]
    public void Parse_SimplePackage_ParsesCorrectly()
    {
        var dep = PythonDependency.Parse("numpy");

        Assert.AreEqual("numpy", dep.Name);
        Assert.IsNull(dep.VersionConstraint);
    }

    [TestMethod]
    public void Parse_PackageWithVersion_ParsesCorrectly()
    {
        var dep = PythonDependency.Parse("numpy>=1.24.0");

        Assert.AreEqual("numpy", dep.Name);
        Assert.AreEqual(">=1.24.0", dep.VersionConstraint);
    }

    [TestMethod]
    public void Parse_PackageWithExactVersion_ParsesCorrectly()
    {
        var dep = PythonDependency.Parse("numpy==1.24.0");

        Assert.AreEqual("numpy", dep.Name);
        Assert.AreEqual("==1.24.0", dep.VersionConstraint);
    }

    [TestMethod]
    public void Parse_PackageWithExtras_ParsesCorrectly()
    {
        var dep = PythonDependency.Parse("requests[security]>=2.28.0");

        Assert.AreEqual("requests", dep.Name);
        Assert.AreEqual(">=2.28.0", dep.VersionConstraint);
        Assert.HasCount(1, dep.Extras);
        Assert.IsTrue(dep.Extras.Contains("security"));
    }

    [TestMethod]
    public void Parse_PackageWithMultipleExtras_ParsesCorrectly()
    {
        var dep = PythonDependency.Parse("uvicorn[standard,websockets]>=0.20.0");

        Assert.AreEqual("uvicorn", dep.Name);
        Assert.AreEqual(">=0.20.0", dep.VersionConstraint);
        Assert.HasCount(2, dep.Extras);
    }

    [TestMethod]
    public void Parse_VersionConstraints_ParsesVariousOperators()
    {
        Assert.AreEqual(">=1.0", PythonDependency.Parse("pkg>=1.0").VersionConstraint);
        Assert.AreEqual("<=2.0", PythonDependency.Parse("pkg<=2.0").VersionConstraint);
        Assert.AreEqual("==1.5", PythonDependency.Parse("pkg==1.5").VersionConstraint);
        Assert.AreEqual("!=1.3", PythonDependency.Parse("pkg!=1.3").VersionConstraint);
        Assert.AreEqual("~=1.4", PythonDependency.Parse("pkg~=1.4").VersionConstraint);
        Assert.AreEqual(">1.0", PythonDependency.Parse("pkg>1.0").VersionConstraint);
        Assert.AreEqual("<2.0", PythonDependency.Parse("pkg<2.0").VersionConstraint);
    }

    [TestMethod]
    public void Parse_EmptyString_ThrowsException()
    {
        Assert.ThrowsExactly<ArgumentException>(() => _ = PythonDependency.Parse(""));
    }

    [TestMethod]
    public void ToString_ReturnsPep508String()
    {
        var dep = new PythonDependency("numpy", ">=1.24.0");

        Assert.AreEqual("numpy>=1.24.0", dep.ToString());
    }
}
