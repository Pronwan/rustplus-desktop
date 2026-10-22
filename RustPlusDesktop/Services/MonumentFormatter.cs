using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RustPlusDesk.Services;

/// <summary>
/// Centralized formatter that transforms internal Rust monument keys and prefab names
/// into human-friendly, properly capitalized display names.
/// </summary>
public static class MonumentFormatter
{
    /// <summary>
    /// Official monument translations (1:1 with the reference client dictionary).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> OfficialMonumentNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["harbor_display_name"] = "Harbor",
        ["harbor_2_display_name"] = "Harbor",
        ["airfield_display_name"] = "Airfield",
        ["excavator"] = "Giant Excavator Pit",
        ["junkyard_display_name"] = "Junkyard",
        ["launchsite"] = "Launch Site",
        ["military_tunnels_display_name"] = "Military Tunnel",
        ["power_plant_display_name"] = "Power Plant",
        ["train_yard_display_name"] = "Train Yard",
        ["water_treatment_plant_display_name"] = "Water Treatment Plant",
        ["lighthouse_display_name"] = "Lighthouse",
        ["bandit_camp"] = "Bandit Camp",
        ["outpost"] = "Outpost",
        ["sewer_display_name"] = "Sewer Branch",
        ["large_oil_rig"] = "Large Oil Rig",
        ["oil_rig_small"] = "Oil Rig",
        ["gas_station"] = "Oxum's Gas Station",
        ["mining_quarry_sulfur_display_name"] = "Sulfur Quarry",
        ["mining_quarry_stone_display_name"] = "Stone Quarry",
        ["mining_quarry_hqm_display_name"] = "HQM Quarry",
        ["satellite_dish_display_name"] = "Satellite Dish",
        ["dome_monument_name"] = "The Dome",
        ["supermarket"] = "Abandoned Supermarket",
        ["mining_outpost_display_name"] = "Mining Outpost",
        ["swamp_c"] = "Abandoned Cabins",
        ["water_well_a_display_name"] = "Water Well",
        ["water_well_b_display_name"] = "Water Well",
        ["water_well_c_display_name"] = "Water Well",
        ["water_well_d_display_name"] = "Water Well",
        ["water_well_e_display_name"] = "Water Well",
        ["large_fishing_village_display_name"] = "Large Fishing Village",
        ["fishing_village_display_name"] = "Fishing Village",
        ["stables_a"] = "Ranch",
        ["stables_b"] = "Large Barn",
        ["train_tunnel_display_name"] = "Train Tunnel",
        ["train_tunnel_link_display_name"] = "Train Tunnel",
        ["underwater_lab"] = "Underwater Labs",
        ["AbandonedMilitaryBase"] = "Abandoned Military Base",
        ["arctic_base_a"] = "Arctic Research Base",
        ["missile_silo_monument"] = "Missile Silo",
        ["ferryterminal"] = "Ferry Terminal",
        ["radtown"] = "Radtown",
        ["jungle_ziggurat"] = "Jungle Ziggurat",
        ["hapis_convoy_display_name"] = "Convoy",
        ["hapis_listening_station"] = "Listening Station",
        ["hapis_sitea_display_name"] = "Site A",
        ["hapis_siteb_display_name"] = "Site B",
        ["hapis_eastlighthouse_display_name"] = "Eastern Lighthouse",
        ["hapis_westlighthouse_display_name"] = "Western Lighthouse",
        ["hapis_abandboat_display_name"] = "Abandoned Boat",
        ["hapis_collapsed_tunnel"] = "Collapsed Tunnel",
        ["hapis_junkpile"] = "Junkyard",
        ["mining_quarry_display_name"] = "Mining Quarry",
        ["hapis_quarry"] = "Pumping Station",
        ["hapis_loadingdock_display_name"] = "Loading Dock",
        ["hapis_ventingshaft_display_name"] = "Venting Shaft",
        ["hapis_tugboatbeached_display_name"] = "Beached Tugboat",
        ["hapis_refinery_refresh"] = "Refinery",
        ["Hapis_outpost_b3"] = "Outpost B3",
    };

    /// <summary>Identifies train tunnel tokens that render as icon discs rather than text labels.</summary>
    public static bool IsTrainTunnel(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var token = Regex.Replace(raw.Trim(), @"^monument\.", "", RegexOptions.IgnoreCase);
        if (token.Equals("train_tunnel_display_name", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("train_tunnel_link_display_name", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        var key = raw.ToLowerInvariant();
        return key.Contains("train tunnel") || key.Contains("tunnel entrance");
    }

    public static string Beautify(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var s = raw.Trim();

        // 0. Filter internal engine scene noise objects (matching reference client 606.js:L756)
        if (s.Equals("DungeonBase", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("Monument", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        // Direct token lookup (with or without 'monument.' prefix)
        var directToken = Regex.Replace(s, @"^monument\.", "", RegexOptions.IgnoreCase);
        if (OfficialMonumentNames.TryGetValue(directToken, out var officialName))
            return officialName;

        // Strip prefab path hierarchy if present: e.g. assets/bundled/prefabs/autospawn/monument/...
        var lastSlash = directToken.LastIndexOfAny(new[] { '/', '\\' });
        var prefabToken = lastSlash >= 0 ? directToken[(lastSlash + 1)..] : directToken;
        prefabToken = Regex.Replace(prefabToken, @"\.prefab$", "", RegexOptions.IgnoreCase);

        if (OfficialMonumentNames.TryGetValue(prefabToken, out var officialPrefabName))
            return officialPrefabName;

        var lower = s.ToLowerInvariant();

        // 2. Specific Monument Normalization
        if (lower.Contains("underwater") || lower.Contains("under water") || lower.Contains("underwaterlab") || lower.Contains("moonpool"))
            return "Underwater Labs";

        if (lower.Contains("dome") || lower.Contains("dome_monument") || lower.Contains("sphere_tank"))
            return "Dome";

        if (lower.Contains("launch facility") || lower.Contains("launch_facility") || lower.Contains("launchsite") || lower.Contains("launch site"))
            return "Launch Site";

        if (lower.Contains("missile silo") || lower.Contains("missile_silo") || lower.Contains("missle silo") || lower.Contains("missle_silo"))
            return "Missile Silo";

        if (lower.Contains("mining quarry sulfur") || lower.Contains("mining_quarry_sulfur") || lower.Contains("quarry_sulfur"))
            return "Sulfur Quarry";

        if (lower.Contains("mining quarry stone") || lower.Contains("mining_quarry_stone") || lower.Contains("quarry_stone"))
            return "Stone Quarry";

        if (lower.Contains("mining quarry hqm") || lower.Contains("mining_quarry_hqm") || lower.Contains("quarry_hqm"))
            return "HQM Quarry";

        if (lower.Contains("large_fishing_village") || lower.Contains("large fishing village") || lower.Contains("fishing_village_large"))
            return "Large Fishing Village";

        if (lower.Contains("fishing_village") || lower.Contains("fishing village"))
        {
            if (Regex.IsMatch(lower, @"\b(a|1)\b") || lower.EndsWith("_a") || lower.EndsWith(" a")) return "Fishing Village A";
            if (Regex.IsMatch(lower, @"\b(b|2)\b") || lower.EndsWith("_b") || lower.EndsWith(" b")) return "Fishing Village B";
            if (Regex.IsMatch(lower, @"\b(c|3)\b") || lower.EndsWith("_c") || lower.EndsWith(" c")) return "Fishing Village C";
            return "Fishing Village";
        }

        if (lower.Contains("mining_outpost") || lower.Contains("mining outpost"))
            return "Mining Outpost";

        if (lower.Contains("train_tunnel") || lower.Contains("train tunnel") || lower.Contains("tunnel_entrance") || lower.Contains("tunnel entrance"))
            return "Train Tunnel";

        if (lower.Contains("trainyard") || lower.Contains("train yard") || lower.Contains("train_yard"))
            return "Train Yard";

        if (lower.Contains("water_treatment") || lower.Contains("water treatment"))
            return "Water Treatment Plant";

        if (lower.Contains("powerplant") || lower.Contains("power plant") || lower.Contains("power_plant"))
            return "Power Plant";

        if (lower.Contains("military tunnels") || lower.Contains("military_tunnels") || lower.Contains("military_tunnel") || lower.Contains("military tunnel"))
            return "Military Tunnels";

        if (lower.Contains("abandoned_military_base") || lower.Contains("abandonedmilitarybase") || lower.Contains("abandoned military base"))
            return "Abandoned Military Base";

        if (lower.Contains("military base") || lower.Contains("military_base"))
            return "Military Base";

        if (lower.Contains("arctic base") || lower.Contains("arctic_base") || lower.Contains("arctic research base") || lower.Contains("arctic_research_base"))
            return "Arctic Research Base";

        if (lower.Contains("oil_rig_2") || lower.Contains("large_oil_rig") || lower.Contains("large oil rig") || lower.Contains("oilrig_2"))
            return "Large Oil Rig";

        if (lower.Contains("oil_rig_1") || lower.Contains("small_oil_rig") || lower.Contains("small oil rig") || lower.Contains("oilrig_1") || lower.Contains("oil rig small"))
            return "Small Oil Rig";

        if (lower.Contains("oil rig") || lower.Contains("oil_rig") || lower.Contains("oilrig"))
            return "Oil Rig";

        if (lower.Contains("supermarket") || lower.Contains("supermarket_1") || lower.Contains("supermarket 1"))
            return "Abandoned Supermarket";

        if (lower.Contains("gas station") || lower.Contains("gas_station") || lower.Contains("oxums"))
            return "Oxum's Gas Station";

        if (lower.Contains("harbor_2") || lower.Contains("harbor 2"))
            return "Harbor";

        if (lower.Contains("harbor_1") || lower.Contains("harbor 1") || lower.Contains("harbor"))
            return "Harbor";

        if (lower.Contains("stables a") || lower.Contains("stables_a") || lower.Contains("ranch"))
            return "Ranch";

        if (lower.Contains("stables b") || lower.Contains("stables_b") || lower.Contains("large barn") || lower.Contains("barn"))
            return "Large Barn";

        if (lower.Contains("excavator") || lower.Contains("giant excavator") || lower.Contains("giant_excavator"))
            return "Large Excavator Pit";

        if (lower.Contains("sewer") || lower.Contains("sewer_branch"))
            return "Sewer Branch";

        if (lower.Contains("apartment complex") || lower.Contains("apartmentcomplex") || lower.Contains("apartment_complex") || lower.Contains("apartments complex"))
            return "Apartments Complex";

        if (lower.Contains("bandit town") || lower.Contains("bandit_town") || lower.Contains("bandit camp") || lower.Contains("bandit_camp"))
            return "Bandit Camp";

        if (lower.Contains("outpost") && !lower.Contains("mining"))
            return "Outpost";

        if (lower.Contains("airfield"))
            return "Airfield";

        if (lower.Contains("lighthouse"))
            return "Lighthouse";

        if (lower.Contains("satellite dish") || lower.Contains("satellite_dish") || lower.Contains("sat dish") || lower.Contains("sat_dish"))
            return "Satellite Dish";

        if (lower.Contains("junkyard") || lower.Contains("junk yard") || lower.Contains("junk_yard"))
            return "Junkyard";

        if (lower.Contains("ferry terminal") || lower.Contains("ferry_terminal") || lower.Contains("ferryterminal"))
            return "Ferry Terminal";

        if (lower.Contains("radtown") || lower.Contains("rad_town"))
            return "Radtown";

        if (lower.Contains("jungle ziggurat") || lower.Contains("jungle_ziggurat") || lower.Contains("ziggurat"))
            return "Jungle Ziggurat";

        if (lower.Contains("ice lake") || lower.Contains("ice_lake"))
            return "Ice Lake";

        if (lower.Contains("water well") || lower.Contains("water_well"))
            return "Water Well";

        if (lower.Contains("swamp"))
            return "Swamp";

        if (lower.Contains("canyon"))
            return "Canyon";

        if (lower.Contains("oasis"))
            return "Oasis";

        // 3. General Token Cleanup for any unmapped or modded monument names
        s = Regex.Replace(s, @"\.prefab$", "", RegexOptions.IgnoreCase);
        s = s.Replace('_', ' ').Replace('-', ' ');
        s = Regex.Replace(s, @"\b(display\s*name|monument\s*name)\b", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\((?:\s*(?:display\s*name|monument\s*name)\s*)\)", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\s+", " ").Trim();

        if (string.IsNullOrWhiteSpace(s))
            return raw.Trim();

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());
    }
}
