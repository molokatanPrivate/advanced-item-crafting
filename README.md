# advanced-item-crafting
User Interface and advanced crafting options for Item Perks and Epic Loot

## Troubleshooting
This plugin will not work without Epic Loot or Item Perks.
One of these plugins has to be installed on the server.

If you find issues during item generation processes and views, its most likely caused by this plugin.
Any functionality of buff effects remains part of the desired plugin.

If there is a Buff missing, you can simply check out the code and add it to the enums.
And then contact me? ;)

## Features
1. Combined Inventory-like View for Epic and Perk Items
2. Advanced Crafting options for Perk Items
3. Standard Crafting for Epic Items
4. Reuse translations and mod configs

## Combined Inventory-like View
When you open the view the first time, you will notice that it looks similar to the players inventory.
One of the main reasons why i implemented that plugin was exactly this feature.
You can inspect each item and see additional informations in one place.

### Player Buff Panel
Ever wanted to know all Epic buffs and Perk buffs affecting you?
Just check out the full list on the left side.
For Perks you can check out descriptions when clicking the info icon (i).

### Selected Items
When you select items, you get a (short) overview of its buff.
For Perk Items it will show a list of all perks and rolls in the description section.

When selecting Epic Items, a new Panel will be shown.
It contains:
- the name of the epic buff
- a description
- its upgrade costs
- the set piece bonus
- the tier ranges
- the chances to hit a certain tier

Quite some information right? :)

### Item Actions
Following the mission, each possible action is right where you would expect them.
Add-, remove- or randomize perks actions are shown in the Action Panel.
For Epic Items you will find add- and recycle-action.
Items other than Epic and Perk Items are NOT supported and most likely never will be supported.

## Advanced Perk Crafting
### Add Perks
(For the basics) players can add perks to existing items.
Weighted Crafting will pick a random mod.
Players can add Kits to that process to increase the chances of picking the desired mod.

chance without kits: 3.3%

| number of Kits	| Recommended Multiplier	| chance	|
|-------------------|---------------------------|-----------|
| 1 Kit				| 15						| 35%		|
| 2 Kits			| 20						| 58%		|
| 3 Kits			| 40						| 80%		|

Depending on the configuration, you can enable perk crafting for epic items and "white" items.
Crafting of perks respects your defined white- and blacklist from Item Perks.
You can define additional costs for this process in the configuration.

### Remove Perks
Players can remove existing perks from an item.
Weighted Crafting will pick a random mod.
Players can add Kits to that process to increase the chances of picking the desired mod.

chance without kits: 33.3%

| number of Kits	| Recommended Multiplier	| chance	|
|-------------------|---------------------------|-----------|
| 1 Kit				| 1							| 50%		|
| 2 Kits			| 1							| 60%		|
| 3 Kits			| 2							| 77%		|

You can define additional costs for this process in the configuration.

### Randomize Perk Values
Players can reroll the values of perks on an item to (maybe) get better values.

You can enable lucky rolls in the configuration.
The player will be able to select 1 Kit for each mod to roll twice.
The better roll will be used for the item.

### Unweighted Crafting for Perks
Players have to select 1 Kit when modifying items.
The kit defines the perk selected for the desired action and will be consumed.

### Weighted Crafting for Perks
Players can select up to 3 Kits when modifying items.
Each kit will add a defined value to the mod weight and increases the chance for the player to hit that mod.
The additional mod weight value is based on the configuration for the desired action.

#### example (no kit):
There are 30 mods in the pool, each with a mod weight of 100.
The weight for BradleyDamage when crafting without any kit is 100.
The total weight of all mods is 3000.

The chance that BradleyDamage gets picked is 100 / 3000 = 0.033 = 3.3%.

#### example (3 kits - 20x):
There are 30 mods in the pool, each with a mod weight of 100.
The multiplier when using 3 kits is configured with 20%.
The player uses 3 kits for BradleyDamage each increasing the mod weight by 2000.
The weight for BradleyDamage when crafting is 4100.
The total weight of all mods is 15000.

The chance that BradleyDamage gets picked is 4100 / 7000 = 0.586 = 58.6%

#### example (3 kits - 40x):
There are 30 mods in the pool, each with a mod weight of 100.
The multiplier when using 3 kits is configured with 40%.
The player uses 3 kits for BradleyDamage each increasing the mod weight by 4000.
The weight for BradleyDamage when crafting is 12100.
The total weight of all mods is 15000.

The chance that BradleyDamage gets picked is 12100 / 15000 = 0.806 = 80.6%

## Permissions

| permission			| description										|
|-----------------------|---------------------------------------------------|
| perk_add				| the player can add perks to an item				|
| perk_remove			| the player can remove perks from an item			|
| perk_randomize		| the player can randomize perk values				|
| perk_bypass_weighting	| the player can use direct crafting for perks		|
| kit2					| the player has 2 kit slots for weighted crafting	|
| kit3					| the player has 3 kit slots for weighted crafting	|
| salvage				| the player can salvage epic items from menu		|
| enhance				| the player can add epic buffs to items			|
| enhance_free			| the player can modify items for free				|

## Translations and Mod Configurations
The plugin reads the Epic Loot and Item Perk configurations when starting.
All weights, chances and mod ranges are used from this configs.

### Crafting Costs
For Epic Loot, the defined upgrade costs are preserved. You dont have to change anything.
Because of the advanced crafting options for perks, this plugin will have its own cost configurations.

### Translations
You will find out when looking into the language file, that this plugin barely uses translations.
One of the reasons is, that it reuses translations from Epic Loot and Item Perks.
If you search for mod descriptions or mod names, please check out the language files of those plugins.