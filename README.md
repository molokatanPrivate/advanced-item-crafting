# advanced-item-crafting
User Interface and advanced crafting options for Item Perks and Epic Loot

## Get Started
You can drop the plugin onto your server, and you are almost good to go.
There are just a few steps to get it running correct.

### Permissions
If you want to use this mod, you should make sure that players dont have permissions listed below.
Otherwise, they can bypass costs or functionality from that plugin.

| plugin     | permission |			 | why?														|
|------------|------------|----------|----------------------------------------------------------|
| Epic Loot  | enhance    | disabled | not needed, if you dont want to use the old screen		|
| Epic Loot  | salvage    | disabled | not needed, if you dont want to use the old screen		|
| Item Perks | enhance    | disabled | this is mandatory, we have our own cost configurations!	|

### Configurations
We have to make sure that players dont see old UI Buttons anymore.

| plugin     | configuration                                                                                    | Value                             |
|------------|--------------------------------------------------------------------------------------------------|-----------------------------------|
| Epic Loot  | "Enable use of the HUD button? Players can still disable it client side via the player settings" | set to **false**, we dont need it |
| Item Perks | "Send the icon to access the ItemPerks menu?"                                                    | set to **false**, we dont need it |

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
When you select items, you get a (short) overview of its buffs.
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

## Perk Crafting
### Add Perks
(For the basics) players can add perks to existing items.
By default the player selects a kit and adds the mod.

For more advanced usage, you can enabled weighted crafting.
This will pick a random mod.
Players can add Kits to that process to increase the chances of picking the desired mod.

chance without kits: 3.3%

| number of Kits	| Recommended Multiplier	| chance	|
|-------------------|---------------------------|-----------|
| 1 Kit				| 15						| 35%		|
| 2 Kits			| 20						| 58%		|
| 3 Kits			| 40						| 80%		|

*chances can be different if you changed the original weighting of mods*

Depending on the configuration, you can enable perk crafting for epic items and "white" items.
Crafting of perks respects your defined white- and blacklist from Item Perks.
You can define additional costs for this process in the configuration.

### Remove Perks
Players can remove existing perks from an item.
By default the player selects a kit and removed the mod.

Weighted Crafting will pick a random mod.
Players can add Kits to that process to increase the chances of picking the desired mod.

chance without kits: 33.3%

| number of Kits	| Recommended Multiplier	| chance	|
|-------------------|---------------------------|-----------|
| 1 Kit				| 1							| 50%		|
| 2 Kits			| 1							| 60%		|
| 3 Kits			| 2							| 77%		|

*chances can be different if you changed the original weighting of mods*

You can define additional costs for this process in the configuration.
If configured, players can remove ALL mods from an item.

### Randomize Perk Values (Advanced Crafting)
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

You can make the selection of 1 kit mandatory for all weighted actions.

Here some examples, how mod weights are working.

#### example (no kit):
There are 30 mods in the pool, each with a mod weight of 100.
The weight for BradleyDamage when crafting without any kit is 100.
The total weight of all mods is 3000.

The chance that BradleyDamage gets picked is 100 / 3000 = 0.033 = 3.3%.

#### example (3 kits - 20x):
There are 30 mods in the pool, each with a mod weight of 100.
The multiplier when using 3 kits is configured with 20x.
The player uses 3 kits for BradleyDamage each increasing the mod weight by 2000.
The weight for BradleyDamage when crafting is 4100.
The total weight of all mods is 15000.

The chance that BradleyDamage gets picked is 4100 / 7000 = 0.586 = 58.6%

#### example (3 kits - 40x):
There are 30 mods in the pool, each with a mod weight of 100.
The multiplier when using 3 kits is configured with 40x.
The player uses 3 kits for BradleyDamage each increasing the mod weight by 4000.
The weight for BradleyDamage when crafting is 12100.
The total weight of all mods is 15000.

The chance that BradleyDamage gets picked is 12100 / 15000 = 0.806 = 80.6%

## Commands

| command			| type		| description												|
|-------------------|-----------|-----------------------------------------------------------|
| aicrafting		| Chat		| Opens Inventory Panel (can be changed in configuration)	|
| cmdopeninventory	| Console	| Opens Inventory Panel										|
nothing else needed ;)

## Permissions
You have several options to customize the user experience by giving different permissions.

A good example is the permissions to make kit slots available for weighted crafting.
Without *perk.kit2* or *perk.kit3* set, the player can only use 1 kit.
You can then give perk.kit2 permission when the player reaches level 20 with skill tree.

| permission				| description										|
|---------------------------|---------------------------------------------------|
| perk.add					| the player can add perks to an item				|
| perk.remove				| the player can remove perks from an item			|
| perk.randomize			| the player can randomize perk values				|
| perk.bypass_weighting		| the player can use direct crafting for perks		|
| perk.kit2					| the player has 2 kit slots for weighted crafting	|
| perk.kit3					| the player has 3 kit slots for weighted crafting	|
| named.unveil				| the player can unveil perks on a named item		|
| epic.salvage				| the player can salvage epic items from menu		|
| epic.enhance				| the player can add epic buffs to items			|
| free						| the player can modify all items for free			|

## Configurations
The plugin is reading configurations from Epic Loot and Item Perk when starting.
All weights, chances and mod ranges are used from this configs.
If you change anything on that configurations, you should restart the plugin.

### Crafting Costs
#### Epic Loot
For Epic Loot, the defined upgrade costs are taken from Epic Loot configuration. You dont have to change anything.

#### Item Perks
Because of the advanced crafting options for perks, this plugin will have its own cost configurations.

You can add default additional costs for each craft type.
Simply set the values for "Item to use when ... a perk".
The default costs is set to use epic scrap.

*If your server is not supporting epic, you should change that.*

Additional costs can be increased for each kit used during crafting process depending on the perk of the kit.

### Named Items
Named items can be restricted and unrestricted.
If restricted, the player cant reveal additional perks.
For unrestricted items, the player can unveil additional perks, if the total amount of perks is not exceeding the max allowed number.
Additional costs can be defined according to other perk crafting actions.

The player will not be able to improve chances with kits.
Kits are disabled and rolls rely fully on perk weights.

## Translations
You will find out when looking into the language file, that this plugin barely uses translations.
One of the reasons is, that it reuses translations from Epic Loot and Item Perks.
If you search for mod descriptions or mod names, please check out the language files of those plugins.

## Troubleshooting
This plugin will not work without Epic Loot or Item Perks.
One of these plugins has to be installed on the server.

If you find issues during item generation processes, payments and views, its most likely caused by this plugin.
Any functionality of buff effects remain part of the original plugin.

If there is a Buff missing, you can simply check out the code and add it to the enums.
And then contact me? ;)