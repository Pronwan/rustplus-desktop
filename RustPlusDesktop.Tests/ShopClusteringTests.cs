using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services;

namespace RustPlusDesktop.Tests;

[TestClass]
public sealed class ShopClusteringTests
{
    private sealed record Shop(uint Id, double X, double Y);

    private static List<List<Shop>> Cluster(IEnumerable<Shop> shops) =>
        ShopClustering.Cluster(shops, s => s.Id, s => s.X, s => s.Y);

    [TestMethod]
    public void NearbyShopsShareACluster()
    {
        var clusters = Cluster(new[] { new Shop(1, 0, 0), new Shop(2, 10, 10), new Shop(3, 500, 500) });

        Assert.AreEqual(2, clusters.Count);
        Assert.AreEqual(2, clusters.Single(c => c.Any(s => s.Id == 1)).Count);
    }

    [TestMethod]
    public void ClustersDoNotDependOnInputOrder()
    {
        var shops = Enumerable.Range(1, 12)
            .Select(i => new Shop((uint)i, i * 20.0, (i % 3) * 15.0))
            .ToList();

        string Describe(IEnumerable<Shop> input) => string.Join(" | ",
            Cluster(input)
                .Select(c => string.Join(",", c.Select(s => s.Id).OrderBy(i => i)))
                .OrderBy(x => x));

        var expected = Describe(shops);
        Assert.AreEqual(expected, Describe(Enumerable.Reverse(shops)));
        Assert.AreEqual(expected, Describe(shops.OrderBy(s => (s.Id * 7919) % 13)));
    }

    [TestMethod]
    public void KeyIsTheLowestIdWhateverTheOrder()
    {
        var a = new[] { new Shop(9, 0, 0), new Shop(4, 5, 5), new Shop(7, 8, 8) };
        var b = a.Reverse().ToArray();

        Assert.AreEqual(4u, ShopClustering.KeyOf(Cluster(a).Single(), s => s.Id));
        Assert.AreEqual(4u, ShopClustering.KeyOf(Cluster(b).Single(), s => s.Id));
    }

    [TestMethod]
    public void EightOrMoreShopsFormOneStableCluster()
    {
        var shops = Enumerable.Range(1, 9).Select(i => new Shop((uint)(100 + i), i, i)).ToList();

        var first = Cluster(shops).Single();
        var second = Cluster(Enumerable.Reverse(shops)).Single();

        Assert.AreEqual(9, first.Count);
        Assert.AreEqual(ShopClustering.KeyOf(first, s => s.Id), ShopClustering.KeyOf(second, s => s.Id));
    }
}
