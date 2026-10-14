using System;
using System.Collections.Generic;
using System.Linq;

namespace RustPlusDesk.Services;

/// <summary>
/// Groups vending machines that stand close together into one map icon.
///
/// The icon for a cluster is kept between polls and looked up by a key, so the key has to
/// stay the same for as long as the cluster does. Two things used to break that: clusters
/// were built greedily in whatever order the API returned the shops, so a shop near the
/// boundary could land in a different cluster from one poll to the next, and the key was the
/// ID of whichever shop happened to come first. Either change removed the icon under the
/// cursor and created a new one, which restarted the hover and made the details popup flicker.
/// Shops are now processed in ID order and keyed by the lowest ID in the cluster.
/// </summary>
public static class ShopClustering
{
    /// <summary>World units within which a shop joins an existing cluster.</summary>
    public const double ClusterDistance = 50.0;

    public static List<List<T>> Cluster<T>(
        IEnumerable<T> items,
        Func<T, uint> id,
        Func<T, double> x,
        Func<T, double> y,
        double maxDistance = ClusterDistance)
    {
        var clusters = new List<List<T>>();

        foreach (var item in items.OrderBy(id))
        {
            List<T>? target = null;
            foreach (var cluster in clusters)
            {
                // Compare against the cluster's average position.
                double dx = cluster.Average(x) - x(item);
                double dy = cluster.Average(y) - y(item);
                if (Math.Sqrt(dx * dx + dy * dy) < maxDistance)
                {
                    target = cluster;
                    break;
                }
            }

            if (target != null) target.Add(item);
            else clusters.Add(new List<T> { item });
        }

        return clusters;
    }

    /// <summary>The key a cluster's map icon is stored under: its lowest shop ID.</summary>
    public static uint KeyOf<T>(IEnumerable<T> cluster, Func<T, uint> id) => cluster.Min(id);
}
