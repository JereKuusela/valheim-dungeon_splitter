using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
namespace DungeonSplitter;

[BepInPlugin(GUID, NAME, VERSION)]
public class DungeonSplitter : BaseUnityPlugin
{
  public const string GUID = "dungeon_splitter";
  public const string NAME = "Dungeon Splitter";
  public const string VERSION = "1.0";
#nullable disable
  public static ConfigEntry<string> configAlwaysSend;
#nullable enable

  public void Awake()
  {
    configAlwaysSend = Config.Bind("General", "Always send", "", "List of object ids that are always sent to clients. Separate with commas.");
    configAlwaysSend.SettingChanged += (sender, args) => DungeonPrefabs.Postfix();
    SetupWatcher();
    new Harmony(GUID).PatchAll();
  }

#pragma warning disable IDE0051
  private void OnDestroy()
  {
    Config.Save();
  }
#pragma warning restore IDE0051
  private void SetupWatcher()
  {
    FileSystemWatcher watcher = new(Path.GetDirectoryName(Config.ConfigFilePath), Path.GetFileName(Config.ConfigFilePath));
    watcher.Changed += ReadConfigValues;
    watcher.Created += ReadConfigValues;
    watcher.Renamed += ReadConfigValues;
    watcher.IncludeSubdirectories = true;
    watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
    watcher.EnableRaisingEvents = true;
  }

  private void ReadConfigValues(object sender, FileSystemEventArgs e)
  {
    if (!File.Exists(Config.ConfigFilePath)) return;
    try
    {
      Config.Reload();
    }
    catch
    {
      Debug.LogWarning("Failed to reload config file");
    }
  }
}

[HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.CreateSyncList))]
public class CreateSyncList
{
  static void Prefix(ZDOMan.ZDOPeer peer)
  {
    FindObjects.InDungeon = peer.m_peer.m_refPos.y >= 3000f;
    FindObjects.SendDungeons = true;
  }
}
[HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.ReleaseNearbyZDOS))]
public class ReleaseNearbyZDOS
{
  static void Prefix(Vector3 refPosition)
  {
    FindObjects.InDungeon = refPosition.y >= 3000f;
    FindObjects.SendDungeons = false;
  }
}
[HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.CreateDestroyObjects))]
public class CreateDestroyObjects
{
  static void Prefix()
  {
    FindObjects.InDungeon = ZNet.instance.GetReferencePosition().y >= 3000f;
    FindObjects.SendDungeons = false;
  }
}
[HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.IsAreaReady))]
public class IsAreaReady
{
  static void Prefix(Vector3 point)
  {
    FindObjects.InDungeon = point.y >= 3000f;
    FindObjects.SendDungeons = false;
  }
}

[HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.FindObjects))]
public class FindObjects
{
  public static HashSet<int> DungeonHashes = [];
  public static HashSet<int> TeleportHashes = [];
  public static bool SendDungeons;
  public static bool InDungeon;
  static bool Prefix(ZDOMan __instance, Vector2i sector, List<ZDO> objects)
  {
    int num = __instance.SectorToIndex(sector);
    if (num >= 0)
    {
      if (__instance.m_objectsBySector[num] != null)
      {
        objects.AddRange(__instance.m_objectsBySector[num].Where(IsOk).ToList());
        return false;
      }
    }
    else if (__instance.m_objectsByOutsideSector.TryGetValue(sector, out var collection))
    {
      objects.AddRange(collection.Where(IsOk).ToList());
    }
    return false;
  }

  public static bool IsOk(ZDO zdo)
  {
    return TeleportHashes.Contains(zdo.m_prefab) || (InDungeon == (zdo.m_position.y >= 3000f)) || (SendDungeons && DungeonHashes.Contains(zdo.m_prefab));
  }
}


[HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.FindDistantObjects))]
public class FindDistantObjects
{
  static bool Prefix(ZDOMan __instance, Vector2i sector, List<ZDO> objects)
  {
    var num = __instance.SectorToIndex(sector);
    if (num >= 0)
    {
      var list = __instance.m_objectsBySector[num];
      if (list == null)
        return false;
      objects.AddRange(list.Where(zdo => zdo.Distant && (zdo.m_position.y >= 3000f) == FindObjects.InDungeon).ToList());
      return false;
    }
    if (__instance.m_objectsByOutsideSector.TryGetValue(sector, out var collection))
    {
      objects.AddRange(collection.Where(zdo => zdo.Distant && (zdo.m_position.y >= 3000f) == FindObjects.InDungeon).ToList());
    }
    return false;
  }
}

[HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Start))]
public class DungeonPrefabs
{
  public static int ProxyHash = "LocationProxy".GetStableHashCode();
  public static void Postfix()
  {
    FindObjects.TeleportHashes = ZNetScene.instance.m_namedPrefabs.Values.Where(prefab => prefab.GetComponentInChildren<Teleport>()).Select(prefab => prefab.name.GetStableHashCode()).ToHashSet();
    FindObjects.TeleportHashes.Add(ProxyHash);

    FindObjects.DungeonHashes = ZNetScene.instance.m_namedPrefabs.Values.Where(prefab => prefab.GetComponent<DungeonGenerator>()).Select(prefab => prefab.name.GetStableHashCode()).ToHashSet();
    var split = DungeonSplitter.configAlwaysSend.Value.Split(',').Select(str => str.Trim()).Where(str => !string.IsNullOrEmpty(str));
    foreach (var str in split)
      FindObjects.DungeonHashes.Add(str.GetStableHashCode());
  }
}