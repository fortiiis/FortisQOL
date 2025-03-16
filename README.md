# FortisQOL

This mod adds 2 new skills. Vitality and Endurance.

Vitality scales when you gather wood, stone, ores, etc...
Endurance scales when you use movement stamina (Run, Jump, Swim)

Vitality benefits:
- Base HP
- Adds Base Health Regeneration that scales with level. Configurable if regen only to base HP amount as of v0.0.2

Endurance benefits:
- Base Stamina
- Base Walk Speed
- Base Run Speed
- Base Swim Speed
- Stamina Regen Amount
- Stamina Regen Delay
- Jump Stamina Cost
- Swim Stamina Cost
- Max Carry Weight

All of these are configurable. Upon first time loading into game "fortis.mods.qolmod" will generate in your BepInEx/config folder

This mod also adds the ability to configure the base rested time at comfort level 1 and the time each comfort level gives

You can also configure item stack sizes, upon loading into a world for the first time (the object database needs to load on world load to get items) the config file will generate entries for each item stack size. 

This mod does work on multiplayer, both the server and players need to have the mod installed. This mod uses [Blaxxun's ServerSync](https://github.com/blaxxun-boop/ServerSync) to allow server admins to toggle locking configurations. Doing so will force the server config values on connected players. Upon disconnecting from the server, your local config values will be restored. ServerSync does not overwrite your config file!

﻿Installation:
- Download [BepInEx](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
- Download dependency [SkillInjector](https://www.nexusmods.com/valheim/mods/341)
- Download latest [Release](https://github.com/fortiiis/FortisQOL/releases)
- Drop FortisQOL.dll into your BepInEx/plugins folder

Config file will generate on first load, item stack config entries will generate first time connecting to a server or loading a single﻿ player world!

Incompatibilities:
- [VitalityRewrite](https://www.nexusmods.com/valheim/mods/1859) - by Gratak
