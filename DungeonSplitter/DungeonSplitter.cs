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
  public const string VERSION = "1.2";
  public const float DungeonHeight = 1500f;
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
    FindObjects.InDungeon = peer.m_peer.m_refPos.y >= DungeonSplitter.DungeonHeight;
    FindObjects.IsSending = true;
  }
}
[HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.ReleaseNearbyZDOS))]
public class ReleaseNearbyZDOS
{
  // Unfortunately the original function doesn't work at all.
  // 1. FindSectorObjects must return all objevts of the nearby zones.
  // 2. InActiveArea checks can't be overridden to include the y coordinate.
  // 3. This is also more performant because FindObjects is not used that appends lists.
  static bool Prefix(ZDOMan __instance, Vector3 refPosition, long uid)
  {
    var zm = __instance;
    FindObjects.InDungeon = refPosition.y >= DungeonSplitter.DungeonHeight;
    FindObjects.IsSending = false;

    Vector2i playerZone = ZoneSystem.instance.GetZone(refPosition);
    int area = ZoneSystem.instance.m_activeArea;
    var xStart = playerZone.x - area;
    var yStart = playerZone.y - area;
    var xEnd = playerZone.x + area;
    var yEnd = playerZone.y + area;
    for (int x = xStart; x <= xEnd; x++)
    {
      for (int y = yStart; y <= yEnd; y++)
      {
        Vector2i sector = new(x, y);
        int num = zm.SectorToIndex(sector);
        if (num >= 0)
        {
          if (zm.m_objectsBySector[num] != null)
            Process(zm, zm.m_objectsBySector[num], sector, uid, FindObjects.InDungeon);
        }
        else if (zm.m_objectsByOutsideSector.TryGetValue(sector, out var list))
          Process(zm, list, sector, uid, FindObjects.InDungeon);
      }
    }
    return false;
  }

  static void Process(ZDOMan zm, List<ZDO> objects, Vector2i zone, long uid, bool inDungeon)
  {
    foreach (ZDO zdo in objects)
    {
      if (!zdo.Persistent) continue;
      // TeleportHashes or DungeonHashes are not used because clients don't need ownership of those objects.
      // If some mod needs it, the client probably already claims the ownership.
      var zdoInDungeon = zdo.m_position.y >= DungeonSplitter.DungeonHeight;
      var sameLevel = zdoInDungeon == inDungeon;
      if (zdo.GetOwner() == uid)
      {
        if (!sameLevel || !ZNetScene.InActiveArea(zdo.GetSector(), zone))
          zdo.SetOwner(0L);
      }
      else if (sameLevel && (!zdo.HasOwner() || !IsInPeerSameLevel(zm, zdoInDungeon, zdo.GetOwner()) || !zm.IsInPeerActiveArea(zdo.GetSector(), zdo.GetOwner())) && ZNetScene.InActiveArea(zdo.GetSector(), zone))
      {
        zdo.SetOwner(uid);
      }
    }
  }

  static bool IsInPeerSameLevel(ZDOMan zm, bool inDungeon, long uid)
  {
    if (uid == zm.m_sessionID) return ZNet.instance.GetReferencePosition().y >= DungeonSplitter.DungeonHeight == inDungeon;
    ZNetPeer peer = ZNet.instance.GetPeer(uid);
    return peer != null && peer.m_refPos.y >= DungeonSplitter.DungeonHeight == inDungeon;
  }
}
[HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.CreateDestroyObjects))]
public class CreateDestroyObjects
{
  static void Prefix()
  {
    FindObjects.InDungeon = ZNet.instance.GetReferencePosition().y >= DungeonSplitter.DungeonHeight;
    FindObjects.IsSending = false;
  }
}
[HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.IsAreaReady))]
public class IsAreaReady
{
  static void Prefix(Vector3 point)
  {
    FindObjects.InDungeon = point.y >= DungeonSplitter.DungeonHeight;
    FindObjects.IsSending = false;
  }
}

[HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.FindObjects))]
public class FindObjects
{
  public static HashSet<int> AlwaysSend = [];
  public static HashSet<int> AlwaysLoad = [];
  public static bool IsSending;
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
    return AlwaysLoad.Contains(zdo.m_prefab) || (InDungeon == (zdo.m_position.y >= DungeonSplitter.DungeonHeight)) || (IsSending && AlwaysSend.Contains(zdo.m_prefab));
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
      objects.AddRange(list.Where(zdo => zdo.Distant && (zdo.m_position.y >= DungeonSplitter.DungeonHeight) == FindObjects.InDungeon).ToList());
      return false;
    }
    if (__instance.m_objectsByOutsideSector.TryGetValue(sector, out var collection))
    {
      objects.AddRange(collection.Where(zdo => zdo.Distant && (zdo.m_position.y >= DungeonSplitter.DungeonHeight) == FindObjects.InDungeon).ToList());
    }
    return false;
  }
}

[HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Start))]
public class DungeonPrefabs
{
  public static int ProxyHash = "LocationProxy".GetStableHashCode();
  public static int PlayerHash = "Player".GetStableHashCode();
  public static void Postfix()
  {
    FindObjects.AlwaysLoad = ZNetScene.instance.m_namedPrefabs.Values.Where(prefab => prefab.GetComponentInChildren<Teleport>()).Select(prefab => prefab.name.GetStableHashCode()).ToHashSet();
    FindObjects.AlwaysLoad.Add(ProxyHash);

    FindObjects.AlwaysSend = ZNetScene.instance.m_namedPrefabs.Values.Where(prefab => prefab.GetComponent<DungeonGenerator>()).Select(prefab => prefab.name.GetStableHashCode()).ToHashSet();
    var split = DungeonSplitter.configAlwaysSend.Value.Split(',').Select(str => str.Trim()).Where(str => !string.IsNullOrEmpty(str));
    foreach (var str in split)
      FindObjects.AlwaysSend.Add(str.GetStableHashCode());
    FindObjects.AlwaysSend.Add(PlayerHash);
  }
}