using Avalonia.Headless;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Avalonia.Shared.Controls;
using RustPlusDesk.Infrastructure.Items;

namespace RustPlusDesk.CrossPlatform.Tests;

[TestClass]
public sealed class ItemIconTests
{
    [TestMethod]
    public async Task All796RequiredInputAndOutputIcons_AreBundledDecodableAndShared()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch(() =>
        {
            var catalog = new BundledRecyclerCatalog().Load();
            string[] keys = catalog.Items.Select(item => item.Resource.ShortName)
                .Concat(catalog.Items.SelectMany(item => item.Wild.Concat(item.Safe)).Select(yield => yield.ShortName))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            Assert.AreEqual(796, keys.Length);
            foreach (string key in keys)
            {
                var icon = ItemIcon.Load(key);
                Assert.IsNotNull(icon, key);
                Assert.AreEqual(40, icon.PixelSize.Width, key);
                Assert.AreSame(icon, ItemIcon.Load(key), key);
            }
            Assert.IsNull(ItemIcon.Load("unknown-item"));
            return true;
        }, CancellationToken.None);
    }
}
