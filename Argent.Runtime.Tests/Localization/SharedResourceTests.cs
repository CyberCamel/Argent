using Argent.Core;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Argent.Runtime.Tests.Localization;

public sealed class SharedResourceTests
{
    [Fact]
    public void Shared_resource_marker_resolves_embedded_resources()
    {
        var factory = new ResourceManagerStringLocalizerFactory(
            Options.Create(new LocalizationOptions()),
            NullLoggerFactory.Instance);

        var localizer = factory.Create(typeof(SharedResource));

        Assert.False(localizer["FormDesigner.Publish"].ResourceNotFound);
        Assert.Equal("Publish", localizer["FormDesigner.Publish"].Value);
    }
}
