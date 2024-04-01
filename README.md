# Dungeon Splitter

Separates dungeons from the main world reducing the amount of instances.

Install on clients, server or both (modding [guide](https://youtu.be/L9ljm2eKLrk)).

## Behavior

This gives some benefit for vanilla, but is mostly intended when using Mineshaft mod or custom dungeons.

Recommended to be at least installed on the server.

When only installed on the server:

- Server only sends objects that are on the same layer (reduces amount of instances and network traffic).
- Clients don't unload objects so most of the benefits are lost after entering a dungeon.

When only installed on the client:

- Server sends all objects so network traffic is not reduced.
- Clients only load objects on the same layer so the performance is improved.
- Client may retain ownership of unloaded objects which can make them appear frozen for other players.

When installed on both:

- Server only sends objects that are on the same layer.
- Clients only load objects on the same layer.

## Exceptions

- LocationProxy is always sent to clients and always loaded. This ensures that dungeon entrances are always loaded.
- Dungeon generators are always sent to clients. This ensures that the dungeon floors are instantly loaded when entering a dungeon.
- For custom dungeons, you have to add the entrance floor to the server config. Otherwise the dungeon can't be entered.
  - For example Ice_floor is commonly used on custom dungeons.

## Credits

Thanks for Azumatt for creating the mod icon!

Sources: [GitHub](https://github.com/JereKuusela/valheim-dungeon_splitter)

Donations: [Buy me a computer](https://www.buymeacoffee.com/jerekuusela)
