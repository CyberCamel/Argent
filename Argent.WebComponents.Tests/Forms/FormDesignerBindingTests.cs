using Argent.Core.Forms.Protocol.V2;
using Argent.WebComponents.Forms.Designer;
using Xunit;

namespace Argent.WebComponents.Tests.Forms;

public sealed class FormDesignerBindingTests
{
    [Fact]
    public void AddObject_uses_protocol_alias_and_preserves_domain_key()
    {
        var designer = new FormDesignerService(null!);

        var first = designer.AddObject("Viewmode POC");
        var second = designer.AddObject("Viewmode POC");

        Assert.Equal("Viewmode_POC", first.Key);
        Assert.Equal("Viewmode_POC2", second.Key);
        Assert.Equal("Viewmode POC", first.ObjectKey);
        Assert.Equal("Viewmode POC", designer.Definition.ObjectKey);
        Assert.True(FormDefinitionCompiler.Compile(designer.Definition).IsValid);
    }
}
