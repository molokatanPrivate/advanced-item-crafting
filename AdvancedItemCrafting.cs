using Facepunch;
using Facepunch.Rust;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Rust;

using Newtonsoft.Json;
using Network;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using System.ComponentModel;
using System.Text;


/**
 * This plugin will not do anything standalone.
 * 
 * You need latest Epic Items and Item Perks installed.
 * - configurations for epic buffs are maintained in Epic Items
 * - epic buff effects are handled by Epic Items
 * - configurations for perk buffs are maintained in Item Perks
 * - perk buff effects are handled by Item Perks
 * - most translations are used from Epic Items and Item Perks to make transformation easier
 * 
 * TODOs by priority:
 * - stacked items?
 * - check permission based buffs
 * - remove inconsitencies when using CuiPanel etc
 * - unweighted actions require their own text
 * - clean up imports
 * - revise UI component names and coords
 * - someone should explain when using Pool stuff and when not. Thought there is a GC (Gen0...)?
 * 
 * possible Features/Ideas
 * - Crafting XP -> used to unlock features?
 * - support for Item Retriever
 * - support for economics and stuff -> might be needed by some servers (even when i dont like it^^)
 **/
namespace Oxide.Plugins
{
    [Info("AdvancedItemCrafting", "molokatan", "0.9.0"), Description("User Interface and advanced crafting options for Item Perks and Epic Loot")]
    class AdvancedItemCrafting : RustPlugin
    {
        [PluginReference]
        private Plugin EpicLoot, ItemPerks, ImageLibrary;
        
        // permissions for perk crafting
        const string perm_perk_add = "advanceditemcrafting.perk_add";
        const string perm_perk_remove = "advanceditemcrafting.perk_remove";
        const string perm_perk_randomize = "advanceditemcrafting.perk_randomize";
        // permission to bypass weight system
        const string perm_perk_bypass_weighting = "advanceditemcrafting.perk_bypass_weighting";

        // amount usable kits for perk crafts if weighted
        const string perm_kit_2 = "advanceditemcrafting.kit2";
        const string perm_kit_3 = "advanceditemcrafting.kit3";

        // permissions for epic items
        const string perm_salvage = "advanceditemcrafting.salvage";
        const string perm_enhance = "advanceditemcrafting.enhance";
        // FIXME: need to check behavior for kits
        const string perm_enhance_free = "advanceditemcrafting.enhance.free";

        // FIXME: this instance should be removed if possible
        public static AdvancedItemCrafting Instance { get; set; }

        #region Hooks
        
        string btn_icon = "assets/icons/inventory.png";

        void OnServerInitialized()
        {
            Instance = this;

            if (config.customButton.enabled && !string.IsNullOrEmpty(config.customButton.Icon))
            {
                if (config.customButton.Icon.StartsWith("http"))
                {
                    if (ImageLibrary == null || !ImageLibrary.IsLoaded)
                    {
                        Puts("Image Library has to be installed to support web images");
                        Interface.Oxide.UnloadPlugin(Name);
                        return;
                    }
                    else
                    {
                        ImageLibrary?.Call("AddImage", config.customButton.Icon, "ai_btn_img");
                    }
                }
                btn_icon = config.customButton.Icon;
            }

            if (EpicLoot == null || !EpicLoot.IsLoaded)
            {
                Puts("You must have Epic Loot installed to run this features.");
                // Interface.Oxide.UnloadPlugin(Name);
            }
            else
            {
                ServerMgr.Instance.StartCoroutine(LoadEpicConfiguration());
            }

            if (ItemPerks == null || !ItemPerks.IsLoaded)
            {
                Puts("You must have Item Perks installed to run this features.");
                // Interface.Oxide.UnloadPlugin(Name);
            }
            else
            {
                ServerMgr.Instance.StartCoroutine(LoadItemPerksConfiguration());
            }
            RegisterPermissions();

            if (BasePlayer.activePlayerList != null && config.customButton.enabled)
            {
                foreach (var player in BasePlayer.activePlayerList)
                    CreateMainButton(player);
            }

            if (!string.IsNullOrEmpty(config.chatCmd))
                cmd.AddChatCommand(config.chatCmd, this, "ChatOpenInventory");
        }
        
        void OnPlayerDeath(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, MAIN_BUTTON);
            CuiHelper.DestroyUi(player, BACKDROP_PANEL);
        }

        void OnPlayerRespawned(BasePlayer player)
        {
            CreateMainButton(player);
        }

        // do we need this?
        /**void OnPlayerConnected(BasePlayer player)
        {
            CreateMainButton(player);
        }**/
        
        void OnPlayerSleepEnded(BasePlayer player)
        {
            CreateMainButton(player);
        }

        void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, MAIN_BUTTON);
                CuiHelper.DestroyUi(player, BACKDROP_PANEL);
            }
            if (!string.IsNullOrEmpty(config.chatCmd))
                cmd.RemoveChatCommand(config.chatCmd, this);
        }
        #endregion Hooks

        #region Permissions
        void RegisterPermissions()
        {
            permission.RegisterPermission(perm_perk_add, this);
            permission.RegisterPermission(perm_perk_remove, this);
            permission.RegisterPermission(perm_perk_randomize, this);
            permission.RegisterPermission(perm_perk_bypass_weighting, this);

            permission.RegisterPermission(perm_kit_2, this);
            permission.RegisterPermission(perm_kit_3, this);

            permission.RegisterPermission(perm_salvage, this);
            permission.RegisterPermission(perm_enhance, this);
            permission.RegisterPermission(perm_enhance_free, this);
        }

        int GetMaxKits(BasePlayer player)
        {
            if (permission.UserHasPermission(player.UserIDString, perm_kit_3)) return 3;
            if (permission.UserHasPermission(player.UserIDString, perm_kit_2)) return 2;
            return 1;
        }
        #endregion Permissions

        #region Commands
        void ChatOpenInventory(BasePlayer player)
        {
            if (player == null) return;

            var playerState = new PlayerState(player);

            var builder = new ExtendedCuiElementContainer();
            CreateInventoryBase(builder, player);
            CreateInventoryItems(builder, playerState);

            AddPlayerBuffsPanel(builder, player, playerState);

            CuiHelper.AddUi(player, builder);
        }

        [ConsoleCommand("cmdopeninventory")]
        void CmdOpenInventory(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            ChatOpenInventory(player);
        }

        [ConsoleCommand("cmdcloseinventory")]
        void CmdCloseInventory(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            
            CuiHelper.DestroyUi(player, BACKDROP_PANEL);
        }

        [ConsoleCommand("cmdselectitem")]
        void CmdSelectItem(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            uint selectedItemUID = Convert.ToUInt32(arg.Args[0]);

            var item = player.inventory.FindItemByUID(new ItemId(selectedItemUID));
            if (item == null) return;

            var baseItem = new BaseItem(item);
            
            var builder = new ExtendedCuiElementContainer();
            CreateItemDetailsBase(builder, player, baseItem);
            CreateItemActions(builder, player, baseItem);
            
            AddEpicBuffDetailsPanel(builder);

            if (baseItem.buff != null)
            {
                EpicBuffDescription(builder, player, baseItem.buff.Buff);
            }

            CuiHelper.AddUi(player, builder);
        }

        [ConsoleCommand("cmdcloseinfobox")]
        void CmdCloseInfoBox(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
        
            CuiHelper.DestroyUi(player, "AI_INFO_BOX");
        }
        
        #region Commands:Epic

        [ConsoleCommand("cmdshowepicbuffselection")]
        void CmdShowEpicBuffSelection(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            uint selectedItemUID = Convert.ToUInt32(arg.Args[0]);
            
            var item = player.inventory.FindItemByUID(new ItemId(selectedItemUID));
            if (item == null) return;
            
            var selectedBuff = Buff.None;
            if (!Enum.TryParse(arg.Args[1], out selectedBuff)) return;

            var opened = Convert.ToBoolean(arg.Args[2]);

            var baseItem = new BaseItem(item);
            
            var builder = new ExtendedCuiElementContainer();
            AddEpicBuffDetailsPanel(builder);
            AddEpicBuffSelector(builder, player, baseItem, selectedBuff, opened);

            if (!opened)
            {
                int cost;
                if(selectedBuff == Buff.None || !epicConfig.scrapper_settings.enhancement_cost.TryGetValue(selectedBuff, out cost)) cost = 0;

                var hasPayment = ItemAmountAvailable(player, epicConfig.scrapper_settings.currency_shortname, epicConfig.scrapper_settings.currency_skin, epicConfig.scrapper_settings.currency_name);
                
                EpicBuffDescription(builder, player, selectedBuff);
                AddEpicBuffConfirmPanel(builder, player, baseItem, selectedBuff, hasPayment >= cost);
            }

            CuiHelper.AddUi(player, builder);
        }

        [ConsoleCommand("cmdconfirmepicselection")]
        void CmdConfirmEpicSelection(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            CuiHelper.DestroyUi(player, EPIC_BUFF_DETAILS_PANEL);

            var selectedItemUID = Convert.ToUInt64(arg.Args[0]);
            var itemToEnhance = player.inventory.FindItemByUID(new ItemId(selectedItemUID));

            if (itemToEnhance == null) return;
            
            var selectedBuff = Buff.None;
            if (!Enum.TryParse(arg.Args[1], out selectedBuff)) return;

            int cost;
            if(selectedBuff == Buff.None || !epicConfig.scrapper_settings.enhancement_cost.TryGetValue(selectedBuff, out cost)) return;

            // FIXME: show an error message if it did not work?
            if (!PayItems(player, epicConfig.scrapper_settings.currency_shortname, epicConfig.scrapper_settings.currency_skin, epicConfig.scrapper_settings.currency_name, cost)) return;

            EpicLoot?.Call<string>("GenerateItem", player, selectedBuff.ToString(), new List<string> { itemToEnhance.info.shortname }, null, true, itemToEnhance);

            // refeshing inventory to remove payments and show the new item
            var playerState = new PlayerState(player);
            var baseItem = new BaseItem(itemToEnhance);

            var builder = new ExtendedCuiElementContainer();
            CreateInventoryBase(builder, player);
            CreateInventoryItems(builder, playerState);
            AddPlayerBuffsPanel(builder, player, playerState);

            CreateItemDetailsBase(builder, player, baseItem);
            CreateItemActions(builder, player, baseItem);
            
            AddEpicBuffDetailsPanel(builder);
            EpicBuffDescription(builder, player, baseItem.buff.Buff);

            CuiHelper.AddUi(player, builder);
        }

        [ConsoleCommand("cmdcancelepicselection")]
        void CmdCancelEpicSelection(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            CuiHelper.DestroyUi(player, EPIC_BUFF_DETAILS_PANEL);
        }
        
        [ConsoleCommand("cmdsalvageepicitem")]
        void CmdSalvageEpicItem(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var selectedItemUID = Convert.ToUInt64(arg.Args[0]);
            var itemToSalvage = player.inventory.FindItemByUID(new ItemId(selectedItemUID));

            if (itemToSalvage == null) return;

            var baseItem = new BaseItem(itemToSalvage);

            int amount;
            if (!epicConfig.scrapper_settings.scrapper_value.TryGetValue(baseItem.buff.Tier, out amount)) return;

            itemToSalvage.RemoveFromContainer();
            var payment = ItemManager.CreateByName(epicConfig.scrapper_settings.currency_shortname, Math.Max(amount, 1), epicConfig.scrapper_settings.currency_skin);
            if (payment == null) return;
            if (!string.IsNullOrEmpty(epicConfig.scrapper_settings.currency_name)) payment.name = epicConfig.scrapper_settings.currency_name;
            player.GiveItem(payment);
            NextTick(() => itemToSalvage.Remove());
            
            // refeshing inventory to remove item and add currency
            var playerState = new PlayerState(player);

            var builder = new ExtendedCuiElementContainer();
            CreateInventoryBase(builder, player);
            CreateInventoryItems(builder, playerState);
            AddPlayerBuffsPanel(builder, player, playerState);

            CuiHelper.AddUi(player, builder);
        }
        
        #endregion Commands:Epic

        #region Commands:Perks
        
        // select kits that increase chance for actions
        [ConsoleCommand("cmdselectperkbuffs")]
        void CmdSelectPerkBuffs(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var selectedItemUID = Convert.ToUInt64(arg.Args[0]);

            var itemToMod = player.inventory.FindItemByUID(new ItemId(selectedItemUID));
            if (itemToMod == null) return;

            string action = arg.Args[1];

            var selectedPerks = CLI.Deserialize<List<Perk>>(arg.Args[2]);
            
            BaseItem baseItem = new BaseItem(itemToMod);

            CraftItem craftItem = GetCraftItem(action);
            int additionalCost = GetCraftItemAmountRequired(craftItem, selectedPerks);
            bool hasPayment = craftItem != null ? CraftItemAmountAvailable(player, craftItem) >= additionalCost : true;
            
            var maxKits = 1;
            if (IsWeightedAction(player, action))
                maxKits = GetMaxKits(player);
            else if (action == "cmdrandomizeperkvalues")
                maxKits = 0;

            string headerText = lang.GetMessage( action.ToUpper() + "_HEADER", this, player.UserIDString);
            string infoText = string.Format(lang.GetMessage( action.ToUpper() + "_INFO", this, player.UserIDString), maxKits);

            ExtendedCuiElementContainer builder = new ExtendedCuiElementContainer();
            SelectPerkBuffsPanel(builder, player, headerText, infoText, baseItem, selectedPerks, maxKits, action, hasPayment);
            SelectKitPanel(builder, player, SELECT_PERK_BUFF_PANEL, 150, baseItem, selectedPerks, maxKits, action);
            if (craftItem != null)
                AdditionalCostPanel(builder, player, SELECT_PERK_BUFF_PANEL, craftItem, additionalCost, hasPayment);

            CuiHelper.AddUi(player, builder);
        }
        
        [ConsoleCommand("cmdcloseselectperkbuffs")]
        void CmdCloseSelectPerkBuffs(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
        
            CuiHelper.DestroyUi(player, SELECT_PERK_BUFF_PANEL);
        }

        [ConsoleCommand("cmdaddperk")]
        void CmdAddPerk(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var selectedItemUID = Convert.ToUInt64(arg.Args[0]);

            var itemToMod = player.inventory.FindItemByUID(new ItemId(selectedItemUID));
            if (itemToMod == null) return;

            var selectedPerks = CLI.Deserialize<List<Perk>>(arg.Args[1]);

            if (IsWeightedAction(player, "cmdaddperk"))
            {
                if (!AddWeightedPerk(player, itemToMod, selectedPerks))
                    return;
            } else if (!AddUnweightedPerk(player, itemToMod, selectedPerks.First())) return;

            // refeshing inventory to remove payments
            var playerState = new PlayerState(player);
            var baseItem = new BaseItem(itemToMod);

            // FIXME: can we reuse open inventory & select item?
            var builder = new ExtendedCuiElementContainer();
            CreateInventoryBase(builder, player);
            CreateInventoryItems(builder, playerState);
            AddPlayerBuffsPanel(builder, player, playerState);

            CreateItemDetailsBase(builder, player, baseItem);
            CreateItemActions(builder, player, baseItem);

            CuiHelper.AddUi(player, builder);
        }

        [ConsoleCommand("cmdrandomizeperkvalues")]
        void CmdRandomizePerkValues(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var selectedItemUID = Convert.ToUInt64(arg.Args[0]);

            var itemToMod = player.inventory.FindItemByUID(new ItemId(selectedItemUID));
            if (itemToMod == null) return;

            var selectedPerks = CLI.Deserialize<List<Perk>>(arg.Args[1]);
            
            if (!RandomizePerkValues(player, itemToMod, selectedPerks)) return;

            // refeshing inventory to remove payments
            var playerState = new PlayerState(player);
            var baseItem = new BaseItem(itemToMod);

            // FIXME: can we reuse open inventory & select item?
            var builder = new ExtendedCuiElementContainer();
            CreateInventoryBase(builder, player);
            CreateInventoryItems(builder, playerState);
            AddPlayerBuffsPanel(builder, player, playerState);

            CreateItemDetailsBase(builder, player, baseItem);
            CreateItemActions(builder, player, baseItem);

            CuiHelper.AddUi(player, builder);
        }
        
        [ConsoleCommand("cmdremoveperk")]
        void CmdRemovePerk(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var selectedItemUID = Convert.ToUInt64(arg.Args[0]);

            var itemToMod = player.inventory.FindItemByUID(new ItemId(selectedItemUID));
            if (itemToMod == null) return;

            var selectedPerks = CLI.Deserialize<List<Perk>>(arg.Args[1]);
            
            if (IsWeightedAction(player, "cmdremoveperk"))
            {
                if (!RemoveWeightedPerk(player, itemToMod, selectedPerks)) return;
            } else if (!RemoveUnweightedPerk(player, itemToMod, selectedPerks.First()));

            // refeshing inventory to remove payments
            var playerState = new PlayerState(player);
            var baseItem = new BaseItem(itemToMod);

            // FIXME: can we reuse open inventory & select item?
            var builder = new ExtendedCuiElementContainer();
            CreateInventoryBase(builder, player);
            CreateInventoryItems(builder, playerState);
            AddPlayerBuffsPanel(builder, player, playerState);

            CreateItemDetailsBase(builder, player, baseItem);
            CreateItemActions(builder, player, baseItem);

            CuiHelper.AddUi(player, builder);
        }

        [ConsoleCommand("cmdshowperkinfopanel")]
        void CmdShowPerkInfoPanel(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var perk = Perk.None;
            if (!Enum.TryParse(arg.Args[0], out perk)) return;

            var builder = new ExtendedCuiElementContainer();
            
            builder.AddInfoBox(lang.GetMessage("UI" + perk.ToString(), ItemPerks, player.UserIDString), GetPerkDescription(player, perk), lang.GetMessage("UI_OK", this, player.UserIDString));

            CuiHelper.AddUi(player, builder);
        }
        #endregion Commands:Perks

        #endregion Commands

        #region Actions

        #region Actions: Perks

        public bool AddUnweightedPerk(BasePlayer player, Item itemToMod, Perk selectedPerk)
        {
            BaseItem baseItem = new BaseItem(itemToMod);

            if (!CanReceivePerkBuff(baseItem)) return false;

            CraftItem craftItem = config.craft_settings.add_perk_settings.craft_item;
            int additionalCost = GetCraftItemAmountRequired(craftItem, new List<Perk> { selectedPerk });
            bool hasPayment = craftItem != null ? CraftItemAmountAvailable(player, craftItem) >= additionalCost : true;

            if (!hasPayment) return false;

            var requiredKits = GetRequiredKitAmount(new List<Perk> { selectedPerk });
            if (!HasKits(player, requiredKits)) return false;

            PerkSettings perkMods;
            if (!perkConfig.enhancementSettings.perk_settings.TryGetValue(selectedPerk, out perkMods)) return false;
            
            if (!perkMods.enabled) return false;

            if (perkMods.blacklist != null && perkMods.blacklist.Count > 0 && perkMods.blacklist.Contains(baseItem.shortname)) return false;

            if (perkMods.whitelist != null && perkMods.whitelist.Count > 0 && !perkMods.whitelist.Contains(baseItem.shortname)) return false;
            
            float mod = (float)Math.Round(UnityEngine.Random.Range(perkMods.min_mod, perkMods.max_mod), 4);
            
            // get payment
            if (craftItem != null)
                if (!PayItems(player, craftItem.shortname, craftItem.skin, craftItem.display_name, additionalCost)) return false;

            if (!PayKits(player, requiredKits)) return false;

            baseItem.perks.Add(new PerkEntry { Perk = selectedPerk, Value = mod });

            // recreate new perk string
            string perkString = "";
            foreach (var perk in baseItem.perks)
            {
                perkString += $"[{perk.Perk} {perk.Value}]";
            }

            if (baseItem.buff == null)
                itemToMod.name = $"{perkConfig.enhancementSettings.item_name_prefix} {itemToMod.info.displayName?.english}";
            
            itemToMod.text = perkString;
            itemToMod.MarkDirty();

            return true;
        }

        public bool AddWeightedPerk(BasePlayer player, Item itemToMod, List<Perk> selectedPerks)
        {
            BaseItem baseItem = new BaseItem(itemToMod);

            if (!CanReceivePerkBuff(baseItem)) return false;

            CraftItem craftItem = config.craft_settings.add_perk_settings.craft_item;
            int additionalCost = GetCraftItemAmountRequired(craftItem, selectedPerks);
            bool hasPayment = craftItem != null ? CraftItemAmountAvailable(player, craftItem) >= additionalCost : true;

            if (!hasPayment) return false;

            var requiredKits = GetRequiredKitAmount(selectedPerks);
            if (!HasKits(player, requiredKits)) return false;

            var perkMultiplier = GetPerkMultiplier(selectedPerks.Count, "cmdaddperk");

            var perkWeights = GetAvailablePerkSettingsForItem(baseItem)
                .ToDictionary(el => el.Key, el => el.Value.perkWeight);
            
            // modify perk weights with selected kits
            foreach (var kit in requiredKits)
                perkWeights[kit.Key] += (int)(perkWeights[kit.Key] * requiredKits[kit.Key] * perkMultiplier);

            var totalWeight = perkWeights.Sum(a => a.Value);

            var weighthit = new System.Random().Next((totalWeight));

            Perk perkToAdd = Perk.None;
            foreach (var perkWeight in perkWeights)
            {
                weighthit -= perkWeight.Value;
                if (weighthit <= 0)
                {
                    perkToAdd = perkWeight.Key;
                    break;
                }
            }
            
            // FIXME: notify that adding perk failed -> NONE selected
            if (perkToAdd.Equals(Perk.None)) return false;

            PerkSettings perkMods;
            if (!perkConfig.enhancementSettings.perk_settings.TryGetValue(perkToAdd, out perkMods)) return false;
            
            float mod = (float)Math.Round(UnityEngine.Random.Range(perkMods.min_mod, perkMods.max_mod), 4);
            
            // get payment
            if (craftItem != null)
                if (!PayItems(player, craftItem.shortname, craftItem.skin, craftItem.display_name, additionalCost)) return false;

            if (!PayKits(player, requiredKits)) return false;
            
            baseItem.perks.Add(new PerkEntry { Perk = perkToAdd, Value = mod });

            // recreate new perk string
            string perkString = "";
            foreach (var perk in baseItem.perks)
            {
                perkString += $"[{perk.Perk} {perk.Value}]";
            }

            if (baseItem.buff == null)
                itemToMod.name = $"{perkConfig.enhancementSettings.item_name_prefix} {itemToMod.info.displayName?.english}";

            itemToMod.text = perkString;
            itemToMod.MarkDirty();

            if (config.craft_settings.add_perk_settings.weight_system.success_effect != null && (selectedPerks.Count == 0 || (selectedPerks.Count > 0 && selectedPerks.Contains(perkToAdd))))
                EffectNetwork.Send(new Effect(config.craft_settings.add_perk_settings.weight_system.success_effect, player.transform.position, player.transform.position), player.net.connection);
            else if (config.craft_settings.add_perk_settings.weight_system.fail_effect != null && selectedPerks.Count > 0)
                EffectNetwork.Send(new Effect(config.craft_settings.add_perk_settings.weight_system.fail_effect, player.transform.position, player.transform.position), player.net.connection);

            return true;
        }

        public bool RemoveUnweightedPerk(BasePlayer player, Item itemToMod, Perk selectedPerk)
        {
            BaseItem baseItem = new BaseItem(itemToMod);

            if (!CanRemovePerkBuff(baseItem)) return false;

            CraftItem craftItem = config.craft_settings.remove_perk_settings.craft_item;
            int additionalCost = GetCraftItemAmountRequired(craftItem, new List<Perk> { selectedPerk });
            bool hasPayment = craftItem != null ? CraftItemAmountAvailable(player, craftItem) >= additionalCost : true;

            if (!hasPayment) return false;

            var requiredKits = GetRequiredKitAmount(new List<Perk> { selectedPerk });
            if (!HasKits(player, requiredKits)) return false;

            PerkSettings perkMods;
            if (!perkConfig.enhancementSettings.perk_settings.TryGetValue(selectedPerk, out perkMods)) return false;
            
            if (!perkMods.enabled) return false;

            var itemPerkKeys = baseItem.perks.Select(el => el.Perk);
            
            // FIXME: notify that removing perk failed -> NONE selected
            if (!itemPerkKeys.Contains(selectedPerk) || selectedPerk.Equals(Perk.None)) return false;
            
            // get payment
            if (craftItem != null)
                if (!PayItems(player, craftItem.shortname, craftItem.skin, craftItem.display_name, additionalCost)) return false;

            if (!PayKits(player, requiredKits)) return false;

            baseItem.perks.RemoveAt(baseItem.perks.FindIndex(i => i.Perk.Equals(selectedPerk)));

            // recreate new perk string
            string perkString = "";
            foreach (var perk in baseItem.perks)
            {
                perkString += $"[{perk.Perk} {perk.Value}]";
            }
            itemToMod.text = perkString;
            itemToMod.MarkDirty();

            return true;
        }

        public bool RemoveWeightedPerk(BasePlayer player, Item itemToMod, List<Perk> selectedPerks)
        {
            BaseItem baseItem = new BaseItem(itemToMod);

            if (!CanRemovePerkBuff(baseItem)) return false;

            CraftItem craftItem = config.craft_settings.remove_perk_settings.craft_item;
            int additionalCost = GetCraftItemAmountRequired(craftItem, selectedPerks);
            bool hasPayment = craftItem != null ? CraftItemAmountAvailable(player, craftItem) >= additionalCost : true;

            if (!hasPayment) return false;

            var requiredKits = GetRequiredKitAmount(selectedPerks);
            if (!HasKits(player, requiredKits)) return false;

            var perkMultiplier = GetPerkMultiplier(selectedPerks.Count, "cmdremoveperk");

            var itemPerkKeys = baseItem.perks.Select(el => el.Perk);

            var perkWeights = GetAvailablePerkSettingsForItem(baseItem)
                .Where(el => itemPerkKeys.Contains(el.Key))
                .ToDictionary(el => el.Key, el => el.Value.perkWeight);
            
            // modify perk weights with selected kits
            foreach (var kit in requiredKits)
                perkWeights[kit.Key] += (int)(perkWeights[kit.Key] * requiredKits[kit.Key] * perkMultiplier);

            var totalWeight = perkWeights.Sum(a => a.Value);

            var weighthit = new System.Random().Next((totalWeight));

            Perk perkToRemove = Perk.None;
            foreach (var perkWeight in perkWeights)
            {
                weighthit -= perkWeight.Value;
                if (weighthit <= 0)
                {
                    perkToRemove = perkWeight.Key;
                    break;
                }
            }
            
            // FIXME: notify that removing perk failed -> NONE selected
            if (perkToRemove.Equals(Perk.None)) return false;
            
            // get payment
            if (craftItem != null)
                if (!PayItems(player, craftItem.shortname, craftItem.skin, craftItem.display_name, additionalCost)) return false;

            if (!PayKits(player, requiredKits)) return false;

            baseItem.perks.RemoveAt(baseItem.perks.FindIndex(i => i.Perk.Equals(perkToRemove)));

            // recreate new perk string
            string perkString = "";
            foreach (var perk in baseItem.perks)
            {
                perkString += $"[{perk.Perk} {perk.Value}]";
            }
            itemToMod.text = perkString;
            itemToMod.MarkDirty();

            if (config.craft_settings.remove_perk_settings.weight_system.success_effect != null && (selectedPerks.Count == 0 || (selectedPerks.Count > 0 && selectedPerks.Contains(perkToRemove))))
                EffectNetwork.Send(new Effect(config.craft_settings.remove_perk_settings.weight_system.success_effect, player.transform.position, player.transform.position), player.net.connection);
            else if (config.craft_settings.remove_perk_settings.weight_system.fail_effect != null && selectedPerks.Count > 0)
                EffectNetwork.Send(new Effect(config.craft_settings.remove_perk_settings.weight_system.fail_effect, player.transform.position, player.transform.position), player.net.connection);

            return true;
        }

        public bool RandomizePerkValues(BasePlayer player, Item itemToMod, List<Perk> selectedPerks)
        {
            BaseItem baseItem = new BaseItem(itemToMod);

            if (baseItem.perks.Count < 1) return false;

            CraftItem craftItem = config.craft_settings.remove_perk_settings.craft_item;
            int additionalCost = GetCraftItemAmountRequired(craftItem, selectedPerks);
            bool hasPayment = craftItem != null ? CraftItemAmountAvailable(player, craftItem) >= additionalCost : true;

            if (!hasPayment) return false;

            var requiredKits = GetRequiredKitAmount(selectedPerks);
            if (!HasKits(player, requiredKits)) return false;
            
            // get payment
            if (craftItem != null)
                if (!PayItems(player, craftItem.shortname, craftItem.skin, craftItem.display_name, additionalCost)) return false;

            if (!PayKits(player, requiredKits)) return false;

            string perkString = string.Empty;
            foreach (var entry in baseItem.perks)
            {
                PerkSettings perkMods;
                if (!perkConfig.enhancementSettings.perk_settings.TryGetValue(entry.Perk, out perkMods)) continue;

                double mod = perkMods.min_mod;
                int rollCount = 1;
                if (requiredKits.TryGetValue(entry.Perk, out int amount) && amount > 0)
                {
                    rollCount++;
                    requiredKits[entry.Perk]--;
                };
                
                for (int i = 0; i<rollCount; i++)
                {
                    var roll = Math.Round(UnityEngine.Random.Range(perkMods.min_mod, perkMods.max_mod), 4);
                    if (roll <= mod) continue;
                    mod = roll;
                }
                
                perkString += $"[{entry.Perk.ToString()} {mod}]";
            }

            itemToMod.text = perkString;
            itemToMod.MarkDirty();

            if (config.craft_settings.randomize_perk_settings.success_effect != null)
                EffectNetwork.Send(new Effect(config.craft_settings.randomize_perk_settings.success_effect, player.transform.position, player.transform.position), player.net.connection);

            return true;
        }

        public bool HasKits(BasePlayer player, Dictionary<Perk, int> requiredAmount)
        {
            if (requiredAmount.Count == 0) return true;

            foreach(var requiredPerk in requiredAmount)
                if (ItemAmountAvailable(player, perkConfig.enhancementSettings.enhancement_kit_settings.shortname, perkConfig.enhancementSettings.enhancement_kit_settings.skin, $"{perkConfig.enhancementSettings.enhancement_kit_settings.displayName} {requiredPerk.Key}") < requiredPerk.Value) return false;
            return true;
        }

        public Dictionary<Perk, int> GetRequiredKitAmount(List<Perk> selectedPerks)
        {
            Dictionary<Perk, int> requiredAmount = new Dictionary<Perk, int>();

            foreach(var perk in selectedPerks)
            {
                if (!requiredAmount.ContainsKey(perk))
                    requiredAmount.Add(perk, 1);
                else
                    requiredAmount[perk]++;
            }
            return requiredAmount;
        }

        public float GetPerkMultiplier(int count, string action)
        {
            WeightRolls actionWeights = null;

            switch (action)
            {
                case "cmdaddperk":
                    actionWeights = config.craft_settings.add_perk_settings.weight_system;
                    break;
                case "cmdremoveperk":
                    actionWeights = config.craft_settings.remove_perk_settings.weight_system;
                    break;
                default:
                    return 1f;
            }

            switch (count)
            {
                case 1:
                    return actionWeights.multiplier_1;
                case 2:
                    return actionWeights.multiplier_2;
                case 3:
                    return actionWeights.multiplier_3;
                default:
                    return 1f;
            }
        }

        public bool IsWeightedAction(BasePlayer player, string action)
        {
            if (permission.UserHasPermission(player.UserIDString, perm_perk_bypass_weighting)) return false;

            switch (action)
            {
                case "cmdaddperk":
                    return config.craft_settings.add_perk_settings.weight_system.enabled;
                case "cmdremoveperk":
                    return config.craft_settings.remove_perk_settings.weight_system.enabled;
                case "cmdrandomizeperkvalues":
                    return config.craft_settings.randomize_perk_settings.allow_lucky_rolls;
            }
            return false;
        }

        public bool RequiresKit(string action)
        {
            switch (action)
            {
                case "cmdaddperk":
                    return config.craft_settings.add_perk_settings.weight_system.requires_kit;
                case "cmdremoveperk":
                    return config.craft_settings.remove_perk_settings.weight_system.requires_kit;
                case "cmdrandomizeperkvalues":
                    return config.craft_settings.randomize_perk_settings.requires_kit;
            }
            return false;
        }
        #endregion Actions: Perks

        #region Actions:Payments

        CraftItem GetCraftItem(string action)
        {
            CraftItem item = null;
            switch (action)
            {
                case "cmdaddperk":
                    return config.craft_settings.add_perk_settings.craft_item;
                case "cmdremoveperk":
                    return config.craft_settings.remove_perk_settings.craft_item;
                case "cmdrandomizeperkvalues":
                    return config.craft_settings.randomize_perk_settings.randomize_perk_item;
                default:
                    return null;
            }
        }

        int GetCraftItemAmountRequired(CraftItem item, List<Perk> selectedPerks)
        {
            if (item == null) return 0;

            int amountRequired = item.amount;
            foreach (Perk perk in selectedPerks)
                if (item.cost_per_kit.TryGetValue(perk, out var value)) amountRequired += value;

            return amountRequired;
        }

        int CraftItemAmountAvailable(BasePlayer player, CraftItem item)
        {
            if (item == null) return 0;
            return ItemAmountAvailable(player, item.shortname, item.skin, item.display_name);
        }

        int ItemAmountAvailable(BasePlayer player, string shortname, ulong skin, string name)
        {
            var definition = ItemManager.FindItemDefinition(shortname);
            var items = player.inventory.FindItemsByItemID(definition.itemid);
            var result = 0;
            foreach (var item in items)
            {
                if (item == null) continue;
                if (skin != null && item.skin != skin) continue;
                if ((string.IsNullOrEmpty(name) && item.name != null) || name != item.name) continue;

                result += item.amount;
            }
            return result;
        }

        public bool PayKits(BasePlayer player, Dictionary<Perk, int> requiredKits)
        {
            foreach (var requiredKit in requiredKits)
                if (!PayKit(player, requiredKit.Key, requiredKit.Value)) return false;

            return true;
        }

        public bool PayKit(BasePlayer player, Perk perk, int amount) => PayItems(player, perkConfig.enhancementSettings.enhancement_kit_settings.shortname, perkConfig.enhancementSettings.enhancement_kit_settings.skin, $"{perkConfig.enhancementSettings.enhancement_kit_settings.displayName} {perk.ToString()}", amount);

        public bool PayItems(BasePlayer player, string shortname, ulong skin, string name, int amount)
        {
            if (permission.UserHasPermission(player.UserIDString, perm_enhance_free)) return true;
            
            // we want to see first if the player has the required amount -> safe remvoe
            if (ItemAmountAvailable(player, shortname, skin, name) < amount) return false;
            
            var definition = ItemManager.FindItemDefinition(shortname);
            var items = player.inventory.FindItemsByItemID(definition.itemid);
            var amountLeft = amount;

            foreach (var item in items)
            {
                if (item == null) continue;
                if (skin != null && item.skin != skin) continue;
                if ((string.IsNullOrEmpty(name) && item.name != null) || name != item.name) continue;

                var taken = item.amount > amountLeft ? item.SplitItem(amountLeft) : item;

                amountLeft -= taken.amount;

                taken.RemoveFromContainer();

                NextTick(() => taken.Remove());

                if (amountLeft <= 0) break;
            }
            return true;
        }

        #endregion Actions:Payments

        #endregion Actions

        #region Classes

        // loading all relevant item infos, so we dont care anymore about the Rust Item. This makes certain actions easier.
        public class BaseItem
        {
            public ItemId uid;

            public string _displayName;
            private string _description;

            public string DisplayName => _displayName;
            public string Description => _description;

            public int itemid;
            public string shortname;
            public ulong skin;
            public string text;

            public int amount;
            public int? ammoCount;

            public float condition;
            public float maxCondition = 0f;

            public List<PerkEntry> perks { get; private set; }
            public EpicEntry buff { get; private set; }

            public int Slot;

            public BaseItem(Item item)
            {
                uid = item.uid;

                if (item.name == item.info.displayName.english || item.name == null)
                    _displayName = item.info.displayName.translated;
                else
                    _displayName = item.name;
                _description = item.info.displayDescription.translated;

                itemid = item.info.itemid;
                skin = item.skin;
                text = item.text;
                shortname = item.info.shortname;

                amount = item.amount;
                ammoCount = item.ammoCount;

                if (item.hasCondition)
                {
                    condition = item.conditionNormalized;
                    maxCondition = item.maxConditionNormalized;
                }

                perks = GetPerks(item);
                buff = GetEpicBuff(item);

                Slot = item.position;
            }

            public bool hasCondition { get { return maxCondition > 0f; } }

            private List<PerkEntry> GetPerks(Item item)
            {
                if (string.IsNullOrWhiteSpace(item.text) || item.text == string.Empty) return new List<PerkEntry>();

                var perkEntries = item.text.Split('[', ']')
                    // ignore everything that is null or empty
                    // we dont need any other validations for it later anymore -> focus on perks only
                    .Where((source, index) => !string.IsNullOrEmpty(source)).ToArray();

                var perks = new List<PerkEntry>();
                foreach (var entry in perkEntries)
                {
                    var split = entry.Split(' ');
                    // any entry that is not having <Perk> and <value> gets ignored
                    if (split.Length <= 1) continue;

                    Perk perk;
                    if (!Enum.TryParse(split[0], out perk)) continue;

                    float value;
                    if (!float.TryParse(split[1], out value)) continue;

                    perks.Add(new PerkEntry { Perk = perk, Value = value });
                }

                return perks;
            }

            private EpicEntry GetEpicBuff(Item item)
            {
                Buff buff = GetEpicBuffType(item.name);

                if (buff == Buff.None) return null;

                var splitString = item.name.Split('[', ']');
                if (splitString.Length < 2) return null;

                return new EpicEntry()
                {
                    Buff = buff,
                    Tier = splitString[1].Split(' ')[0].Trim(),
                    Value = splitString[1].Split(' ')[1].Trim(),
                };
            }
        }

        // building the state of the player looks a bit like overkill, but easier to handle.
        public class PlayerState
        {
            public string playerUid;

            public Dictionary<string, BaseItem> InventoryItems = new Dictionary<string, BaseItem>();
            public Dictionary<string, BaseItem> WearItems = new Dictionary<string, BaseItem>();
            public Dictionary<string, BaseItem> BeltItems = new Dictionary<string, BaseItem>();

            public Dictionary<Buff, float> activeEpicBuffs = new Dictionary<Buff, float>();
            public Dictionary<Buff, int> setBonus = new Dictionary<Buff, int>();

            public Dictionary<Perk, float> activePerkBuffs = new Dictionary<Perk, float>();

            public string selectedItemUid;
            public BaseItem activeItem;

            public PlayerState(BasePlayer player)
            {
                playerUid = player.UserIDString;

                foreach(var mainItem in player.inventory.containerMain.itemList)
                    InventoryItems.Add(mainItem.uid.ToString(), new BaseItem(mainItem));

                foreach(var wearItem in player.inventory.containerWear.itemList)
                    WearItems.Add(wearItem.uid.ToString(), new BaseItem(wearItem));

                foreach (var beltItem in player.inventory.containerBelt.itemList)
                    BeltItems.Add(beltItem.uid.ToString(), new BaseItem(beltItem));

                var item = player.GetActiveItem();
                if (item != null)
                    activeItem = new BaseItem(item);

                SetActiveEpicBuffs();
                SetActivePerkBuffs();
            }

            private void SetActiveEpicBuffs()
            {
                var items = new List<BaseItem>();
                items.AddRange(WearItems.Values);
                if (activeItem != null)
                    items.Add(activeItem);

                foreach(var item in items)
                {
                    var epicBuff = item.buff;
                    if (epicBuff == null)
                        continue;

                    if (!activeEpicBuffs.ContainsKey(epicBuff.Buff))
                        activeEpicBuffs.Add(epicBuff.Buff, 0);

                    activeEpicBuffs[epicBuff.Buff] = Convert.ToSingle(epicBuff.Value) + activeEpicBuffs[epicBuff.Buff];
                    
                    if (!setBonus.ContainsKey(epicBuff.Buff))
                        setBonus.Add(epicBuff.Buff, 0);
                    
                    setBonus[epicBuff.Buff] = setBonus[epicBuff.Buff]+1;
                }
            }

            private void SetActivePerkBuffs()
            {
                var items = new List<BaseItem>();
                items.AddRange(WearItems.Values);
                if (activeItem != null)
                    items.Add(activeItem);

                foreach(var item in items)
                {
                    foreach(var perk in item.perks)
                    {
                        if (!activePerkBuffs.ContainsKey(perk.Perk))
                            activePerkBuffs.Add(perk.Perk, 0);

                        activePerkBuffs[perk.Perk] += Convert.ToSingle(perk.Value);
                    }
                }
            }
        }

        #endregion Classes

        #region Items

        #region Items:Epic
        public enum Buff
        {
            None,
            Miners, // Mining yield
            Lumberjacks, // woodcutting yield
            Skinners, // Skinning yield
            Farmers, // Grown harvesting yield
            Foragers, // Map generated harvesting yield
            Fishermans, // Fishing yield
            Assassins, // Increased PVP damage
            Demo, // Decreased damage from explosives
            Elemental, // Decreased damage from fire/cold/radiation
            Scavengers, // Chance to obtain scrap from barrels/chests.
            Transporters, // Reduced fuel consumption in helis and boats.
            Crafters, // Increased crafting speed
            Reinforced, // Reduces durability loss
            Tamers, // Reduced damage from animals
            Hunters, // Increased damage to animals
            Operators, // Increased damage to scientists
            Jockeys, // Increases speed of horse
            Raiders, // Chance for thrown explosive or rocket to replaced on use.
            Builders, // Chance for build and upgrade costs to be refunded.
            Assemblers, // Chance for components to be refunded.
            Fabricators, // Chance to create an additional item on craft.
            Medics, // Increase healing
            Knights, // Melee damage reduction
            Barbarians, // Increased damage with melee weapons

            BonusMultiplier, // Multiple the bonus of the item.
            Smelting, // Instantly smelt resources as you mine them
            InstantMining, // INstantly mine a node out
            InstantWoodcutting, // Instantly cut a tree down
            Regrowth, // Instantly respawn a tree at the same location
            InstantSkinning, // Instantly butcher an animal
            InstantCook, // Instantly cooks meat
            PVPCrit, // Chance to do critical damage in PVP
            Reflexes, // Reduced damage in PVP
            IncreasedBoatSpeed, // Increase boat speed
            FreeVehicleRepair, // Repair vehicles for free
            Survivalist, // Increases cal/hyd from food/water
            Researcher, // Refund or partial refund of scrap when researching
            Feline, // Reduces fall damage
            Lead, // Reduces radiation damage
            Gilled, // Underwater breathing
            Smasher, // % chance to destroy barells and roadsigns instantly
            WoodcuttersLuck, // Access to a loot table for woodcutting.
            MinersLuck, //access to a loot table for mining.
            SkinnersLuck, // access to a loot table for skinning.
            RockCycle, // chance to spawn a new rock once mined out.
            Attractive, // chance for loot to be instantly moved to your inventory.
            FishersLuck, // acces to a loot table for fishing.
            TeamHeal, // Shares heals with nearby team mates
            HealthShot, // Heals team mates for damage that would have been done when shot
            BulletProof, // Reduces damage from bullets.
            FishingRodModifier, // Adjusts the tensile strenght of the cast rod
            UncannyDodge, // Chance to dodge projectiles and receive no damage 
            MaxHealth // Increases the max health of the player.
        }

        public class EpicEntry
        {
            public Buff Buff;
            public string Value;
            public string Tier;
        }

        public static Buff GetEpicBuffType(string name)
        {
            if (string.IsNullOrEmpty(name)) return Buff.None;
            var buffStr = name.Split(' ')?.First() ?? null;
            if (string.IsNullOrEmpty(buffStr)) return Buff.None;
            Buff value;
            if (Enum.TryParse(buffStr, out value)) return value;
            return Buff.None;
        }

        public bool CanReceiveEpicBuff(BaseItem item) {
            if (item.buff != null) return false;
            if (item.perks.Count > 0 && !config.craft_settings.add_perk_settings.perksForEpic) return false;

            if (!EpicEnhanceableItems.Contains(item.shortname) || epicConfig.skin_blacklist.Contains(item.skin))
                return false;

            return GetAvailableEpicBuffsForItem(item).Count > 0;
        }

        public List<Buff> GetAvailableEpicBuffsForItem(BaseItem item)
        {
            List<Buff> availableBuffs = new List<Buff>();
            foreach(var buff in Enum.GetValues(typeof(Buff)).Cast<Buff>())
            {
                EnhancementInfo ei;
                if (!epicConfig.enhancements.TryGetValue(buff, out ei) || !ei.enabled || (ei.item_whitelist != null && ei.item_whitelist.Count > 0 && !ei.item_whitelist.Contains(item.shortname)) || (ei.item_blacklist != null && ei.item_blacklist.Contains(item.shortname))) continue;
                availableBuffs.Add(buff);
            }
            return availableBuffs;
        }

        public string GetSetBonusDescription(BasePlayer player, Buff set)
        {
            return lang.GetMessage($"Description.SetBonus.{set}", EpicLoot, player.UserIDString);            
        }

        public string GetScrapperCurrencyName()
        {
            if (string.IsNullOrEmpty(epicConfig.scrapper_settings.currency_name))
            {
                return ItemManager.FindItemDefinition(epicConfig.scrapper_settings.currency_shortname)?.displayName.english?.TitleCase() ?? epicConfig.scrapper_settings.currency_shortname;
            }
            else return epicConfig.scrapper_settings.currency_name.TitleCase();
        }
        
        #endregion

        #region Items:Perks
        public enum Perk
        {
            None,
            Prospector, // Mining yield ++
            Lumberjack, // Woodcutting yield ++
            Butcher, // Skinning yield ++
            Horticulture, // Farming yield +
            Forager, // Harvesting yield ++
            Angler, // Fishing yield ++
            BeastBane, // More damage to animals ++
            ScientistBane, // More damage to scientists ++
            FlakJacket, // Reduced damage from explosions  ++
            Elemental, // Reduced damage from elements (fire/cold)  ++
            Scavenger, // Chance to find additional scrap in crates/barrels.  ++
            //Hybrid, // Reduced fuel consumption
            Manufacture, // Crafting speed ++
            Durable, // Reduces durability loss ++
            BeastWard, // Reduce damage from animals  ++
            ScientistWard, // Reduced damage from scientists  ++
            //Equestrian, // Horse speed            
            Builder, // Chance to refund materials used to upgrade building blocks ++
            Thrifty, // Chance to refund crafting components ++
            Fabricate, // Chance to duplicate the crafted item ++
            Pharmaceutical, // Increased healing ++
            MeleeWard, // Reduced melee damage  ++
            //Sails, // increased boat speed
            Academic, // chance to refund research cost ++
            FallDamage, // Reduces fall damage  ++
            Lead, // Reduces radiation damage  ++
            //Gilled, // Breath underwater
            Smasher, // Chance to smash barrels and road signs instantly. Find a better name.  ++
            Environmentalist, // Recycler speed ++
            Smelter, // Smelt speed ++
            Paramedic, // Reviver - increase health of player you are reviving ++
            Prepper, // Rationer - chance to not consume food ++
            Regeneration, // Health regen ++
            SharkWard, // Shark resist  ++
            SharkBane, // Shark bane  ++
            // Untie speed reduction
            Deforest, // tree clear out (like WC ultimate) ++
            BlastMine, // node clear out (same as WC, but for nodes) ++
            Tanner, // skin clear out (same as WC, but for bodies) ++
            Vampiric, // Vampiric ++
            Reinforced, // Vehicle damage reduction ++
            // skin cook
            ComponentLuck, // Component luck ++
            ElectronicsLuck, // electrical luck ++
            UncannyDodge, // Chance to receive no damage from attack ++
            LineStrength, // Fishing rod strength ++
            HealShare, // Heals nearby team members when you heal ++
            Attractive, //Loot magnet ++
            WoodcuttingLuck, // random components from woodcutting on FinalHit ++
            MiningLuck, // random components from mining on FinalHit ++
            SkinningLuck, // random components from skinning on FinalHit ++
            FishingLuck, // random components from fishing on catch ++
            Sated, // More cal/hydration from food
            IronStomach, // Chance for no poison or cal/water reduction when eating
            // Vehicle repair costs reduction
            // Explosives refund chance perk
            // Melee damage
            // Smelting mined ore
            // Instant mining
            // Instant woodcutting
            // Instant skinning
            TreePlanter, // Tree regrowth when cut down ++
            RockCycler, // Node respawn when mined out ++
            BradleyDamage,
            HeliDamage,
        }

        public class PerkEntry
        {
            public Perk Perk;
            public float Value;
        }

        public bool CanReceivePerkBuff(BaseItem item)
        {
            if (item.perks.Count >= config.craft_settings.add_perk_settings.maxPossiblePerks) return false;

            if (item.buff != null && !config.craft_settings.add_perk_settings.perksForEpic) return false;

            if (item.buff == null && item.perks.Count == 0 && !config.craft_settings.add_perk_settings.perksForBlankItems) return false;

            if (!PerkEnhanceableItems.Contains(item.shortname))
                return false;

            return GetAvailablePerksForItem(item).Count > 0;
        }

        public bool CanRemovePerkBuff(BaseItem item) => item.perks.Count > (config.craft_settings.remove_perk_settings.canRemoveAllPerks ? 0 : 1);

        List<Perk> GetAvailablePerksForItem(BaseItem item) => GetAvailablePerkSettingsForItem(item).Keys.ToList();

        Dictionary<Perk, PerkSettings> GetAvailablePerkSettingsForItem(BaseItem item)
        {
            return perkConfig.enhancementSettings.perk_settings
                .Where(setting => {
                    var value = setting.Value;
                    
                    if (!value.enabled) return false;

                    if (value.blacklist != null && value.blacklist.Count > 0 && value.blacklist.Contains(item.shortname)) return false;

                    if (value.whitelist != null && value.whitelist.Count > 0 && !value.whitelist.Contains(item.shortname)) return false;

                    return true;
                }).ToDictionary(el => el.Key, el => el.Value);
        }

        // FIXME: maybe write own translations, so we can have our own format
        string GetPerkDescription(BasePlayer player, Perk perk)
        {
            PerkSettings ps;
            if (!perkConfig.enhancementSettings.perk_settings.TryGetValue(perk, out ps)) return lang.GetMessage(perk.ToString(), ItemPerks, player.UserIDString);
            return string.Format(lang.GetMessage(perk.ToString(), ItemPerks, player.UserIDString), GetPerkValue(ps.min_mod, perk), GetPerkValue(ps.max_mod, perk), lang.GetMessage(GetPerkTypeString(perk), ItemPerks, player.UserIDString)) + (ps.perk_cap > 0 ? string.Format(lang.GetMessage("PerkPlayerLimit", ItemPerks, player.UserIDString), GetPerkValue(ps.perk_cap, perk)) : null) + (string.Format(lang.GetMessage("UI_PERK_DETAILS_WEIGHT", this, player.UserIDString), ps.perkWeight));
        }

        string GetPerkTypeString(Perk perk)
        {
            switch (perk)
            {
                case Perk.Regeneration: return " hps";
                default: return "%";
            }
        }

        double GetPerkValue(float mod, Perk perk)
        {
            switch (perk)
            {
                case Perk.Regeneration: return Math.Round(mod,3);
                default: return Math.Round(mod * 100, 2);
            }
        }
        
        public Dictionary<Perk, int> getAvailableKits(BasePlayer player)
        {            
            var definition = ItemManager.FindItemDefinition(perkConfig.enhancementSettings.enhancement_kit_settings.shortname);
            var found = player.inventory.FindItemsByItemID(definition.itemid).Where((source, index) => source.skin == perkConfig.enhancementSettings.enhancement_kit_settings.skin && source.name.StartsWith(perkConfig.enhancementSettings.enhancement_kit_settings.displayName, StringComparison.OrdinalIgnoreCase));

            Dictionary<Perk, int> available = new Dictionary<Perk, int>();
            foreach (var kit in found)
            {
                var perk = getPerkFromKitName(kit.name);
                if (!available.ContainsKey(perk))
                    available[perk] = 0;
                available[perk] = kit.amount + available[perk];
            }
            return available;
        }
        
        public Perk getPerkFromKitName(string name)
        {
            Perk value = Perk.None;
            return (Enum.TryParse(name.Replace(perkConfig.enhancementSettings.enhancement_kit_settings.displayName, "").Trim(), out value)) ? value : Perk.None;
        }
        #endregion

        #endregion Items


        #region UIBuilder
        public void CreateMainButton(BasePlayer player)
        {
            if (!config.customButton.enabled) return;

            ExtendedCuiElementContainer builder = new ExtendedCuiElementContainer();
            builder.Add(new CuiPanel { Image = { Color = config.customButton.BackgroundColor }, RectTransform = { AnchorMin = config.customButton.AnchorMin, AnchorMax = config.customButton.AnchorMax, OffsetMin = config.customButton.OffsetMin, OffsetMax = config.customButton.OffsetMax } }, config.customButton.Parent, MAIN_BUTTON);
            
            if (btn_icon.IsNumeric())
                builder.Add(new CuiElement { Name = $"{MAIN_BUTTON}_Img", Parent = MAIN_BUTTON, Components = { new CuiImageComponent { Color = "1 1 1 1", ItemId = 1776460938, SkinId = Convert.ToUInt64(btn_icon) }, new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"0 0", OffsetMax = $"0 0" } } });
            else if (btn_icon.StartsWith("http"))
                builder.Add(new CuiElement { Name = $"{MAIN_BUTTON}_Img", Parent = MAIN_BUTTON, Components = { new CuiRawImageComponent { Color = "1 1 1 1", Png = (string)ImageLibrary?.Call("GetImage", "ai_btn_img") }, new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"0 0", OffsetMax = $"0 0" } } });
            else
                builder.Add(new CuiElement { Name = $"{MAIN_BUTTON}_Img", Parent = MAIN_BUTTON, Components = { new CuiImageComponent { Color = "1 1 1 1", Sprite = btn_icon }, new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "2 2", OffsetMax = "-2 -2" } } });

            builder.Add(new CuiButton { Button = { Color = "0 0 0 0", Command = "cmdopeninventory" }, Text = { Text = "" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" } }, MAIN_BUTTON, $"{MAIN_BUTTON}_Btn");
            
            CuiHelper.AddUi(player, builder);
        }

        public void CreateInventoryBase(ExtendedCuiElementContainer builder, BasePlayer player)
        {
            // backdrop
            builder.Add(new CuiPanel { Image = { Color = "0 0 0 0.95", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat", Sprite = "assets/content/ui/ui.background.gradient.psd" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "-0.351 -0.332", OffsetMax = "0.349 0.338" }, CursorEnabled = true, KeyboardEnabled = true }, "Overlay", BACKDROP_PANEL, BACKDROP_PANEL);
            
            // close button
            builder.Add(new CuiPanel { Image = { Color = "0.969 0.922 0.882 0.11", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "-383 76", OffsetMax = "-213 112" } }, BACKDROP_PANEL, CLOSE_BUTTON);
            builder.Add(new CuiButton { Button = { Color = "0.3 0.3 0.3 1", Command = CLOSE_COMMAND }, Text = { Text = lang.GetMessage("UICLOSE", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 20, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "10 5", OffsetMax = "-10 -5" } }, CLOSE_BUTTON, $"{CLOSE_BUTTON}_Btn");

            // belt slots
            builder.Add(new CuiPanel { RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "-200 18", OffsetMax = "185 18" } }, BACKDROP_PANEL, BELT_PANEL);
            for (int i = 0; i < 6; i++)
                builder.Add(new CuiElement { Parent = BELT_PANEL, Name = $"{BELT_ITEM_SLOT}_{i}", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.035", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = $"{64 * i} 0", OffsetMax = $"{(64 * i) + 60} 60" } } });
                    
            // wear slots
            builder.Add(new CuiPanel { RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "-587 117", OffsetMax = "-213 117" } }, BACKDROP_PANEL, WEAR_PANEL);
            for (int i = 0; i < 7; i++)
                builder.Add(new CuiElement { Parent = WEAR_PANEL, Name = $"{WEAR_ITEM_SLOT}_{i}", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.035", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = $"{54 * i} 0", OffsetMax = $"{(54 * i) + 50} 50" } } });

            // backpack slot
            builder.Add(new CuiElement { Parent = WEAR_PANEL, Name = $"{WEAR_ITEM_SLOT}_7", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.035", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = "0 54", OffsetMax = "50 104" } } });

            // inventory slots
            builder.Add(new CuiPanel { RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "-200 87", OffsetMax = "185 87" } }, BACKDROP_PANEL, INVENTORY_PANEL);
            builder.Add(new CuiElement { Name = INVENTORY_TITLE, Parent = INVENTORY_PANEL, Components = { new CuiTextComponent { Text = lang.GetMessage("UI_INVENTORY", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 20, Align = TextAnchor.LowerLeft, Color = "1 1 1 1" }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "0 250", OffsetMax = "0 275" } } });

            for (int i = 0; i < 24; i++)
                builder.Add(new CuiElement { Parent = INVENTORY_PANEL, Name = $"{INVENTORY_ITEM_SLOT}_{i}", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.035", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "0 0", OffsetMin = $"{64 * (i % 6)} {64 * (3 - (int)Math.Floor(i / 6f))}", OffsetMax = $"{64 * (i % 6) + 60} {(64 * (3 - (int)Math.Floor(i / 6f))) + 60}" } } });
        }

        public void CreateInventoryItems(ExtendedCuiElementContainer builder, PlayerState playerState)
        {
            foreach (var item in playerState.BeltItems.Values)
                builder.AddItemButton(item, 50, $"{COMMAND_SELECT_ITEM} {item.uid.Value}", $"{BELT_ITEM_SLOT}_{item.Slot}", $"{ITEM_WRAPPER}_{item.uid.Value}");

            foreach (var item in playerState.WearItems.Values)
                builder.AddItemButton(item, 50, $"{COMMAND_SELECT_ITEM} {item.uid.Value}", $"{WEAR_ITEM_SLOT}_{item.Slot}", $"{ITEM_WRAPPER}_{item.uid.Value}");

            foreach (var item in playerState.InventoryItems.Values)
                builder.AddItemButton(item, 50, $"{COMMAND_SELECT_ITEM} {item.uid.Value}", $"{INVENTORY_ITEM_SLOT}_{item.Slot}", $"{ITEM_WRAPPER}_{item.uid.Value}");
        }

        public void AddPlayerBuffsPanel(ExtendedCuiElementContainer builder, BasePlayer player, PlayerState playerState)
        {
            builder.Add(new CuiPanel { Image = { Color = "0 0 0 0.5" }, RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "0 0", OffsetMax = "0 0" } }, BACKDROP_PANEL, PLAYER_BUFFS_PANEL, PLAYER_BUFFS_PANEL);
            builder.Add(new CuiLabel { Text = { Text = lang.GetMessage("UI_PLAYERBUFFS_TITLE", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 18, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }, RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = $"-587 589", OffsetMax = $"-213 612" } }, PLAYER_BUFFS_PANEL, "PLAYER_BUFFS_PANEL_TITLE" );

            var offset = 0;

            // add set buffs to epic buff values
            var epicBuffs = playerState.activeEpicBuffs;
            var permissions = "";
            foreach(var setBonus in playerState.setBonus)
            {
                var set = epicConfig.enhancements[setBonus.Key];

                SetBonusEffect pieceBonuses = null;

                foreach (var setInfo in set.setInfo)
                    if (setInfo.Key > setBonus.Value)
                        continue;
                    else
                        pieceBonuses = setInfo.Value;

                // not enough pieces for a bonus
                if (pieceBonuses == null)
                    continue;

                foreach(var pieceBonus in pieceBonuses.setBonus)
                {
                    if (pieceBonus.Value.perms != null)
                        foreach (var perm in pieceBonus.Value.perms)
                            permissions += permissions == "" ? perm.Value : $", {perm.Value}";

                    if (pieceBonus.Key.Equals(Buff.BonusMultiplier))
                    {
                        epicBuffs[setBonus.Key] = pieceBonus.Value.modifier + epicBuffs[setBonus.Key];
                        continue;
                    }
                    
                    if (!epicBuffs.ContainsKey(pieceBonus.Key))
                        epicBuffs.Add(pieceBonus.Key, 0);

                    epicBuffs[pieceBonus.Key] = epicBuffs[pieceBonus.Key] + pieceBonus.Value.modifier;
                }
            }

            var innerContainer = new ExtendedCuiElementContainer();
            innerContainer.Add(new CuiElement { Name = "AI_PLAYER_BUFFS_DETAILS", Parent = "AI_PLAYER_BUFFS_DETAILS_SB", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.11", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"0 0", OffsetMax = $"0 0" } } });
            
            innerContainer.Add(new CuiElement { Name = $"HeaderEpicDescription", Parent = "AI_PLAYER_BUFFS_DETAILS", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.11", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"0 -{20 + offset}", OffsetMax = $"0 -{offset}" } } });
            innerContainer.Add(new CuiLabel { Text = { Text = lang.GetMessage("UI_PLAYERBUFFS_EPIC", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"1 1", OffsetMin = $"6 0", OffsetMax = $"-6 0" } }, $"HeaderEpicDescription", "HeaderEpicDescription_Text" );
            
            offset += 23;

            if (epicBuffs.Count == 0 && permissions.Length == 0)
            {
                innerContainer.Add(new CuiElement { Name = $"BonusDescriptionEpicNone", Parent = "AI_PLAYER_BUFFS_DETAILS", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"10 -{33 + offset}", OffsetMax = $"0 -{offset}" } } });
                innerContainer.Add(new CuiLabel { Text = { Text = lang.GetMessage("UI_PLAYERBUFFS_NO_EPIC", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 0.3" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"1 1", OffsetMin = $"3 0", OffsetMax = $"-3 0" } }, $"BonusDescriptionEpicNone", $"BonusDescriptionEpicNone_Text" );

                offset += 34;
            }

            foreach (var epic in epicBuffs.OrderBy(p => p.Key.ToString()))
            {
                var col = GetColorFromHtml("#077E93");
                
                innerContainer.Add(new CuiElement { Name = $"BonusDescription{epic.Key.ToString()}", Parent = "AI_PLAYER_BUFFS_DETAILS", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0.3 1", OffsetMin = $"10 -{33 + offset}", OffsetMax = $"0 -{offset}" } } });
                innerContainer.Add(new CuiLabel { Text = { Text = lang.GetMessage(epic.Key.ToString(), EpicLoot, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = $"{col.r} {col.g} {col.b} {col.a}" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"1 1", OffsetMin = $"3 0", OffsetMax = $"-3 0" } }, $"BonusDescription{epic.Key.ToString()}", $"BonusDescription_Text{epic.Key.ToString()}" );

                innerContainer.Add(new CuiElement { Name = $"BonusDescription{epic.Key.ToString()}", Parent = "AI_PLAYER_BUFFS_DETAILS", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0.3 1", AnchorMax = "1 1", OffsetMin = $"0 -{33 + offset}", OffsetMax = $"-10 -{offset}" } } });
                innerContainer.Add(new CuiLabel { Text = { Text = string.Format(GetSetBonusDescription(player, epic.Key), epic.Value * 100), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"1 1", OffsetMin = $"3 0", OffsetMax = $"-3 0" } }, $"BonusDescription{epic.Key.ToString()}", $"BonusDescription_Text{epic.Key.ToString()}" );
            
                offset += 34;
            }

            if (permissions != "")
            {                
                innerContainer.Add(new CuiElement { Name = $"BonusDescriptionPermissions", Parent = "AI_PLAYER_BUFFS_DETAILS", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0.3 1", OffsetMin = $"10 -{33 + offset}", OffsetMax = $"0 -{offset}" } } });
                innerContainer.Add(new CuiLabel { Text = { Text = lang.GetMessage("UI_EPICBUFFDESCRIPTION_SET_BONU_PERMISSION", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = $"1 1 1 1" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"1 1", OffsetMin = $"3 0", OffsetMax = $"-3 0" } }, $"BonusDescriptionPermissions", $"BonusDescriptionPermissions_Text" );
                
                innerContainer.Add(new CuiElement { Name = $"BonusDescriptionPermissionsText", Parent = "AI_PLAYER_BUFFS_DETAILS", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0.3 1", AnchorMax = "1 1", OffsetMin = $"0 -{33 + offset}", OffsetMax = $"-10 -{offset}" } } });
                innerContainer.Add(new CuiLabel { Text = { Text = permissions, Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"1 1", OffsetMin = $"3 0", OffsetMax = $"-3 0" } }, $"BonusDescriptionPermissionsText", $"BonusDescriptionPermissionsText_Text" );
            
                offset += 34;
            }
            
            offset+= 8;
            
            innerContainer.Add(new CuiElement { Name = $"HeaderPerkDescription", Parent = "AI_PLAYER_BUFFS_DETAILS", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.11", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"0 -{20 + offset}", OffsetMax = $"0 -{offset}" } } });
            innerContainer.Add(new CuiLabel { Text = { Text = lang.GetMessage("UI_PLAYERBUFFS_PERKS", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"1 1", OffsetMin = $"6 0", OffsetMax = $"-6 0" } }, $"HeaderPerkDescription", "HeaderPerkDescription_Text" );
            
            offset += 23;

            if (playerState.activePerkBuffs.Count == 0)
            {
                innerContainer.Add(new CuiElement { Name = $"BonusDescriptionPerkNone", Parent = "AI_PLAYER_BUFFS_DETAILS", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"10 -{33 + offset}", OffsetMax = $"0 -{offset}" } } });
                innerContainer.Add(new CuiLabel { Text = { Text = lang.GetMessage("UI_PLAYERBUFFS_NO_PERK", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 0.3" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"1 1", OffsetMin = $"3 0", OffsetMax = $"-3 0" } }, $"BonusDescriptionPerkNone", $"BonusDescriptionPerkNone_Text" );

                offset += 34;
            }

            foreach (var perk in playerState.activePerkBuffs.OrderBy(p => p.Key.ToString()))
            {
                var col = GetColorFromHtml("#077E93");

                innerContainer.Add(new CuiElement { Name = $"BonusDescription{perk.Key.ToString()}", Parent = "AI_PLAYER_BUFFS_DETAILS", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0.3 1", OffsetMin = $"10 -{23 + offset}", OffsetMax = $"0 -{offset}" } } });
                innerContainer.Add(new CuiLabel { Text = { Text = lang.GetMessage("UI" + perk.Key.ToString(), ItemPerks, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = $"{col.r} {col.g} {col.b} {col.a}" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"1 1", OffsetMin = $"3 0", OffsetMax = $"-3 0" } }, $"BonusDescription{perk.Key.ToString()}", $"BonusDescription_Text{perk.Key.ToString()}" );

                innerContainer.Add(new CuiElement { Name = $"BonusDescription{perk.Key.ToString()}", Parent = "AI_PLAYER_BUFFS_DETAILS", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0.3 1", AnchorMax = "1 1", OffsetMin = $"0 -{23 + offset}", OffsetMax = $"-50 -{offset}" } } });
                innerContainer.Add(new CuiLabel { Text = { Text = $"{GetPerkValue(perk.Value, perk.Key)}{GetPerkTypeString(perk.Key)}", Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"1 1", OffsetMin = $"3 0", OffsetMax = $"-3 0" } }, $"BonusDescription{perk.Key.ToString()}", $"BonusDescription_Text{perk.Key.ToString()}" );
            
                innerContainer.Add(new CuiElement { Name = $"BonusDescriptionBtn{perk.Key.ToString()}", Parent = "AI_PLAYER_BUFFS_DETAILS", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = $"-50 -{23 + offset}", OffsetMax = $"-10 -{offset}" } } });
                innerContainer.Add(new CuiPanel { Image = { Color = "1 1 1 1", Sprite = "assets/icons/info.png" }, RectTransform = { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = $"8 -8", OffsetMax = $"24 8" } }, $"BonusDescriptionBtn{perk.Key.ToString()}", $"BonusDescriptionBtn{perk.Key.ToString()}_ICON");
                innerContainer.Add(new CuiButton { Button = { Color = "0 0 0 0", Command = $"cmdshowperkinfopanel {perk.Key.ToString()}" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" } }, $"BonusDescriptionBtn{perk.Key.ToString()}", $"BonusDescriptionBtn{perk.Key.ToString()}_BTN");

                offset += 24;
            }

            // scroll panel container

            builder.Add(new CuiElement
            {
                Name = "AI_PLAYER_BUFFS_DETAILS_SB",
                Parent = PLAYER_BUFFS_PANEL,
                Components = {
                    new CuiScrollViewComponent {
                        MovementType = UnityEngine.UI.ScrollRect.MovementType.Elastic,
                        Vertical = true,
                        Inertia = true,
                        Horizontal = false,
                        Elasticity = 0.25f,
                        DecelerationRate = 0.3f,
                        ScrollSensitivity = 24f,
                        ContentTransform = new CuiRectTransform { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"0 -{Math.Max(offset, 364)}", OffsetMax = "-5 0" },
                        VerticalScrollbar = new CuiScrollbar() { Size = 2f, AutoHide = true },
                    },
                    new CuiRawImageComponent { Color = "0 0 0 0" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = $"-587 225", OffsetMax = $"-213 589" }
                }
            });

            builder.AddRange(innerContainer);
        }

        #region UIBuilder:ItemDetails
        // item details without actions
        public void CreateItemDetailsBase(ExtendedCuiElementContainer builder, BasePlayer player, BaseItem item)
        {
            builder.Add(new CuiPanel { Image = { Color = "0 0 0 0" }, RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = $"-200 363", OffsetMax = $"180 625" } }, BACKDROP_PANEL, ITEM_DETAILS_CONTAINER, ITEM_DETAILS_CONTAINER);

            builder.Add(new CuiElement { Name = "AI_ITEM_NAME", Parent = ITEM_DETAILS_CONTAINER, Components = { new CuiTextComponent { Text = item.DisplayName.ToUpper(), Font = "robotocondensed-bold.ttf", FontSize = 20, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }, new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 30" } } });
            builder.Add(new CuiElement { Name = "AI_ITEM_DESCRIPTION", Parent = ITEM_DETAILS_CONTAINER, Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"0 -60", OffsetMax = $"0 0" } } });
            builder.Add(new CuiLabel { Text = { Text = item.Description, Font = "robotocondensed-regular.ttf", FontSize = 10, Align = TextAnchor.UpperLeft, Color = "1 1 1 1" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"5 5", OffsetMax = $"-65 -5" } }, "AI_ITEM_DESCRIPTION", "AI_ITEM_DESCRIPTION_TEXT");

            // icon
            builder.Add(new CuiPanel { Image = { Color = "0 0 0 0" }, RectTransform = { AnchorMin = "1 0", AnchorMax = "1 1", OffsetMin = $"-60 0", OffsetMax = $"0 0" } }, "AI_ITEM_DESCRIPTION", "AI_ITEM_DESCRIPTION_ICON_WRAPPER");
            builder.AddItemIcon(item, 60, "AI_ITEM_DESCRIPTION_ICON_WRAPPER", "AI_ITEM_DESCRIPTION_ICON");
            
            // item info section
            builder.Add(new CuiElement { Name = "AI_ITEM_INFO", Parent = ITEM_DETAILS_CONTAINER, Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"0 0", OffsetMax = $"-160 -60" } } });
            builder.Add(new CuiElement { Name = "AI_ITEM_INFO_HEADER", Parent = "AI_ITEM_INFO", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"0 -22", OffsetMax = $"0 -2" } } });
            builder.Add(new CuiLabel { Text = { Text = lang.GetMessage( "UI_ITEM_DETAILS_DESCRIPTION", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"5 0", OffsetMax = $"-5 0" } }, "AI_ITEM_INFO_HEADER", "AI_ITEM_INFO_HEADER_TEXT");
            
            if (item.perks.Count > 0)
                AddPerkInfo(builder, player, item.perks, "AI_ITEM_INFO", 50);
            
            // item actions section
            builder.Add(new CuiElement { Name = "AI_ITEM_ACTIONS", Parent = ITEM_DETAILS_CONTAINER, Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "1 0", AnchorMax = "1 0", OffsetMin = $"-158 0", OffsetMax = $"0 178" } } });
            builder.Add(new CuiElement { Name = "AI_ITEM_ACTIONS_HEADER", Parent = ITEM_DETAILS_CONTAINER, Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.11", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "1 0", AnchorMax = "1 0", OffsetMin = $"-158 180", OffsetMax = $"0 200" } } });
            builder.Add(new CuiLabel { Text = { Text = lang.GetMessage( "UI_ITEM_DETAILS_ACTIONS", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"3 0", OffsetMax = $"-3 0" } }, "AI_ITEM_ACTIONS_HEADER", "AI_ITEM_ACTIONS_HEADER_TITLE");
        }

        public void CreateItemActions(ExtendedCuiElementContainer builder, BasePlayer player, BaseItem item)
        {
            int offset = 0;
            if (item.perks.Count > 0 || ((item.buff == null ? config.craft_settings.add_perk_settings.perksForBlankItems : config.craft_settings.add_perk_settings.perksForEpic) && GetAvailablePerksForItem(item).Count > 0))
            {
                if (config.craft_settings.randomize_perk_settings.enabled && permission.UserHasPermission(player.UserIDString, perm_perk_randomize))
                {
                    if (item.perks.Count > 0)
                        builder.AddActionButton(lang.GetMessage( "UI_ITEM_DETAILS_RANDOMIZE_PERKS", this, player.UserIDString), offset, "assets/icons/gear.png", $"cmdselectperkbuffs {item.uid.Value} cmdrandomizeperkvalues {CLI.Serialize(new List<Perk>())}", ITEM_ACTIONS_CONTAINER, "RANDOMIZE_PERK");
                    offset += 33;
                }

                if (config.craft_settings.remove_perk_settings.enabled && permission.UserHasPermission(player.UserIDString, perm_perk_remove))
                {
                    if (CanRemovePerkBuff(item))
                        builder.AddActionButton(lang.GetMessage( "UI_ITEM_DETAILS_REMOVE_PERK", this, player.UserIDString), offset, "assets/icons/deauthorize.png", $"cmdselectperkbuffs {item.uid.Value} cmdremoveperk {CLI.Serialize(new List<Perk>())}", ITEM_ACTIONS_CONTAINER, "REMOVE_PERK");
                    offset += 33;
                }

                if (config.craft_settings.add_perk_settings.enabled && permission.UserHasPermission(player.UserIDString, perm_perk_add))
                {
                    if (CanReceivePerkBuff(item))
                        builder.AddActionButton( lang.GetMessage( "UI_ITEM_DETAILS_ADD_PERK_BUFF", this, player.UserIDString), offset, "assets/icons/authorize.png", $"cmdselectperkbuffs {item.uid.Value} cmdaddperk {CLI.Serialize(new List<Perk>())}", ITEM_ACTIONS_CONTAINER, "ADD_PERK");
                    offset += 33;
                }
            }

            if (item.buff != null)
            {
                if (permission.UserHasPermission(player.UserIDString, perm_salvage))
                    builder.AddActionButton(lang.GetMessage( "UI_ITEM_DETAILS_RECYCLE_EPIC", this, player.UserIDString), offset, "assets/icons/gear.png", $"cmdsalvageepicitem {item.uid.Value}", ITEM_ACTIONS_CONTAINER, "RECYCLE");
            }
            else
            {
                if (permission.UserHasPermission(player.UserIDString, perm_enhance) && CanReceiveEpicBuff(item))
                    builder.AddActionButton(lang.GetMessage( "UI_ITEM_DETAILS_ADD_EPIC_BUFF", this, player.UserIDString), offset, "assets/icons/authorize.png", $"cmdshowepicbuffselection {item.uid.Value} None true", ITEM_ACTIONS_CONTAINER, "ADD_EPIC_BUFF");
            } 
        }

        #endregion UIBuilder:ItemDetails

        #region UIBuilder:Epic

        // base panel for epic buff selection and details (not item description!)
        public void AddEpicBuffDetailsPanel(ExtendedCuiElementContainer builder)
        {
            builder.Add(new CuiPanel { Image = { Color = "0 0 0 0" }, RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "0 0", OffsetMax = "0 0" } }, BACKDROP_PANEL, EPIC_BUFF_DETAILS_PANEL, EPIC_BUFF_DETAILS_PANEL);
        }

        // dropdown to select epic buff
        public void AddEpicBuffSelector(ExtendedCuiElementContainer builder, BasePlayer player, BaseItem item, Buff buff, bool opened)
        {
            builder.Add(new CuiElement { Name = "AI_EPIC_BUFF_DROPDOWN", Parent = EPIC_BUFF_DETAILS_PANEL, Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = $"193 593", OffsetMax = $"411 625" } } });

            // selected buff
            builder.Add(new CuiElement { Name = "AI_EPIC_BUFF_DROPDOWN_SELECTED", Parent = "AI_EPIC_BUFF_DROPDOWN", Components = { new CuiRawImageComponent { Color = "0 0 0 0", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"3 1", OffsetMax = $"-26 -1" } } });
            builder.Add(new CuiElement { Name = "AI_EPIC_BUFF_DROPDOWN_SELECTED_TEXT", Parent = "AI_EPIC_BUFF_DROPDOWN_SELECTED", Components = { new CuiTextComponent { Text = lang.GetMessage(buff.ToString(), EpicLoot, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }, new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"3 0", OffsetMax = $"-3 0" } } });
            // dropdown indicator
            builder.Add(new CuiElement { Name = "AI_EPIC_BUFF_DROPDOWN_ICON", Parent = "AI_EPIC_BUFF_DROPDOWN", Components = { new CuiRawImageComponent { Color = "0 0 0 0", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "1 0", AnchorMax = "1 1", OffsetMin = $"-23 6", OffsetMax = $"-3 -6" } } });
            builder.Add(new CuiPanel { Image = { Color = "1 1 1 1", Sprite = opened ? "assets/icons/circle_closed.png" : "assets/icons/circle_open.png" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"0 0", OffsetMax = $"0 0" } }, "AI_EPIC_BUFF_DROPDOWN_ICON", "AI_EPIC_BUFF_DROPDOWN_ICON_PNG");
                
            if (buff != Buff.None)
                builder.Add(new CuiButton { Button = { Color = "0 0 0 0", Command = $"cmdshowepicbuffselection {item.uid.Value} {buff.ToString()} {!opened}" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" } }, "AI_EPIC_BUFF_DROPDOWN", $"AI_EPIC_BUFF_DROPDOWN_BTN");
                
            if (opened)
            {
                var innerContainer = new CuiElementContainer();
                var offset = 3;
                foreach (var b in GetAvailableEpicBuffsForItem(item).OrderBy(i => i.ToString()))
                {
                    int cost;
                    if(!epicConfig.scrapper_settings.enhancement_cost.TryGetValue(b, out cost)) continue;

                    innerContainer.Add(new CuiElement { Name = "AI_EPIC_BUFF_DROPDOWN_OPTION", Parent = "AI_EPIC_BUFF_DROPDOWN_SB_BG", Components = { new CuiRawImageComponent { Color = "0 0 0 0.5", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"3 -{32 + offset}", OffsetMax = $"-3 -{offset}" } } });
                    innerContainer.Add(new CuiElement { Name = "AI_EPIC_BUFF_DROPDOWN_OPTION_TEXT", Parent = "AI_EPIC_BUFF_DROPDOWN_OPTION", Components = { new CuiTextComponent { Text = lang.GetMessage(b.ToString(), EpicLoot, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = (b == buff ? "1 1 1 1" : "1 1 1 1") }, new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"3 0", OffsetMax = $"-3 0" } } });
                    innerContainer.Add(new CuiButton { Button = { Color = "0 0 0 0", Command = $"cmdshowepicbuffselection {item.uid.Value} {b.ToString()} {false}" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" } }, "AI_EPIC_BUFF_DROPDOWN_OPTION", $"BTN");

                    offset += 33;
                }
                offset += 2;

                builder.Add(new CuiElement
                {
                    Name = "AI_EPIC_BUFF_DROPDOWN_SB",
                    Parent = "AI_EPIC_BUFF_DROPDOWN",
                    Components = {
                        new CuiScrollViewComponent {
                            MovementType = UnityEngine.UI.ScrollRect.MovementType.Elastic,
                            Vertical = true,
                            Inertia = true,
                            Horizontal = false,
                            Elasticity = 0.25f,
                            DecelerationRate = 0.3f,
                            ScrollSensitivity = 24f,
                            ContentTransform = new CuiRectTransform { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"0 -{Math.Max(offset, 248)}", OffsetMax = "0 0" },
                            VerticalScrollbar = new CuiScrollbar() { Size = 2f, AutoHide = true },
                        },
                        new CuiRawImageComponent { Color = "0 0 0 0" },
                        new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"10 -{Math.Min(400, (offset+26))}", OffsetMax = $"-10 -32" }
                    }
                });
                builder.Add(new CuiElement { Name = "AI_EPIC_BUFF_DROPDOWN_SB_BG", Parent = "AI_EPIC_BUFF_DROPDOWN_SB", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"0 -{offset-3}", OffsetMax = $"0 3" } } });

                builder.AddRange(innerContainer);
            }
        }

        // scrollable panel with description and details about a specific epic buff 
        public void EpicBuffDescription(ExtendedCuiElementContainer builder, BasePlayer player, Buff selectedBuff)
        {
            var set = epicConfig.enhancements[selectedBuff];
            var offset = 0;
            int cost;
            if(!epicConfig.scrapper_settings.enhancement_cost.TryGetValue(selectedBuff, out cost)) cost = 0;

            // scroll content container
            var innerContainer = new ExtendedCuiElementContainer();

            innerContainer.Add(new CuiElement { Name = "AI_EPIC_BUFF_DESCRIPTION", Parent = "AI_EPIC_BUFF_DESCRIPTION_SB", Components = { new CuiRawImageComponent { Color = "0 0 0 0", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"0 0", OffsetMax = $"0 0" } } });

            // item bonus description and cost header

            innerContainer.Add(new CuiElement { Name = $"HeaderSetDescription", Parent = "AI_EPIC_BUFF_DESCRIPTION", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.11", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"0 -{20 + offset}", OffsetMax = $"-60 -{offset}" } } });
            innerContainer.Add(new CuiLabel { Text = { Text = lang.GetMessage("UI_EPICBUFFDESCRIPTION_DESCRIPTION", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"1 1", OffsetMin = $"6 0", OffsetMax = $"-6 0" } }, $"HeaderSetDescription", "HeaderSetDescription_Text" );

            innerContainer.Add(new CuiElement { Name = $"HeaderSetCost", Parent = "AI_EPIC_BUFF_DESCRIPTION", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.11", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = $"-58 -{20 + offset}", OffsetMax = $"0 -{offset}" } } });
            innerContainer.Add(new CuiLabel { Text = { Text = lang.GetMessage("UI_EPICBUFFDESCRIPTION_COST", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"1 1", OffsetMin = $"6 0", OffsetMax = $"-6 0" } }, $"HeaderSetCost", "HeaderSetCost_Text" );

            offset += 23;

            // item bonus description and cost

            innerContainer.Add(new CuiElement { Name = $"SetDescription", Parent = "AI_EPIC_BUFF_DESCRIPTION", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"10 -{48 + offset}", OffsetMax = $"-60 -{offset}" } } });
            innerContainer.Add(new CuiLabel { Text = { Text = lang.GetMessage("Info"+selectedBuff.ToString(), EpicLoot, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"1 1", OffsetMin = $"6 3", OffsetMax = $"-6 -3" } }, $"SetDescription", $"SetDescription_Text" );

            var costItem = ItemManager.CreateByName(epicConfig.scrapper_settings.currency_shortname, cost, epicConfig.scrapper_settings.currency_skin);

            innerContainer.Add(new CuiElement { Name = $"SetCost", Parent = "AI_EPIC_BUFF_DESCRIPTION", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = $"-58 -{48 + offset}", OffsetMax = $"-10 -{offset}" } } });
            innerContainer.AddItemIconWithAmount(new BaseItem(costItem), 48f, "SetCost", "SetCost_Item");

            offset += 58;

            // set bonus
            innerContainer.Add(new CuiElement { Name = $"HeaderSetBonus", Parent = "AI_EPIC_BUFF_DESCRIPTION", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.11", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"0 -{20 + offset}", OffsetMax = $"0 -{offset}" } } });
            innerContainer.Add(new CuiLabel { Text = { Text = lang.GetMessage("UI_EPICBUFFDESCRIPTION_SET_BONUS", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"1 1", OffsetMin = $"6 0", OffsetMax = $"-6 0" } }, $"HeaderSetBonus", "HeaderSetBonus_Text" );

            offset += 23;

            foreach (var setInfo in set.setInfo)
            {
                innerContainer.Add(new CuiElement { Name = $"Header{setInfo.Key}", Parent = "AI_EPIC_BUFF_DESCRIPTION", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"10 -{20 + offset}", OffsetMax = $"-10 -{offset}" } } });
                innerContainer.Add(new CuiLabel { Text = { Text = string.Format(lang.GetMessage("UI_EPICBUFFDESCRIPTION_SET_PIECES", this, player.UserIDString), setInfo.Key), Font = "robotocondensed-bold.ttf", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"1 1", OffsetMin = $"6 0", OffsetMax = $"-6 0" } }, $"Header{setInfo.Key}", "Header_Text" );
                offset += 22;

                foreach (var s in setInfo.Value.setBonus)
                {
                    var SetBonusDescription = string.Format(lang.GetMessage("UI_EPICBUFFDESCRIPTION_SET_BONUS_DESCRIPTION", this, player.UserIDString), lang.GetMessage(s.Key.ToString(), EpicLoot, player.UserIDString), string.Format(GetSetBonusDescription(player, s.Key), s.Value.modifier * 100));
                    var col = GetColorFromHtml("#077E93");
                    innerContainer.Add(new CuiElement { Name = $"BonusDescription{s.Key.ToString()}", Parent = "AI_EPIC_BUFF_DESCRIPTION", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0.3 1", OffsetMin = $"10 -{33 + offset}", OffsetMax = $"0 -{offset}" } } });
                    innerContainer.Add(new CuiLabel { Text = { Text = lang.GetMessage(s.Key.ToString(), EpicLoot, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = $"{col.r} {col.g} {col.b} {col.a}" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"1 1", OffsetMin = $"3 0", OffsetMax = $"-3 0" } }, $"BonusDescription{s.Key.ToString()}", $"BonusDescription_Text{s.Key.ToString()}" );

                    innerContainer.Add(new CuiElement { Name = $"BonusDescriptionText{s.Key.ToString()}", Parent = "AI_EPIC_BUFF_DESCRIPTION", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0.3 1", AnchorMax = "1 1", OffsetMin = $"0 -{33 + offset}", OffsetMax = $"-10 -{offset}" } } });
                    innerContainer.Add(new CuiLabel { Text = { Text = string.Format(GetSetBonusDescription(player, s.Key), s.Value.modifier * 100), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"1 1", OffsetMin = $"3 0", OffsetMax = $"-3 0" } }, $"BonusDescriptionText{s.Key.ToString()}", $"BonusDescription_Text{s.Key.ToString()}" );

                    offset += 34;

                    // FIXME: we should move the permissions to the end of set piece bonus list and simply combine all to one string

                    if (s.Value.perms != null && s.Value.perms.Count > 0)
                    {
                        foreach (var perm in s.Value.perms)
                        {
                            innerContainer.Add(new CuiElement { Name = $"BonusDescription{s.Key.ToString()}_{perm.Value}", Parent = "AI_EPIC_BUFF_DESCRIPTION", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0.3 1", OffsetMin = $"10 -{28 + offset}", OffsetMax = $"0 -{offset}" } } });
                            innerContainer.Add(new CuiLabel { Text = { Text = lang.GetMessage("UI_EPICBUFFDESCRIPTION_SET_BONU_PERMISSION", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = $"{col.r} {col.g} {col.b} {col.a}" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"1 1", OffsetMin = $"3 0", OffsetMax = $"-3 0" } }, $"BonusDescription{s.Key.ToString()}_{perm.Value}", $"BonusDescription_Text{s.Key.ToString()}_{perm.Value}" );

                            innerContainer.Add(new CuiElement { Name = $"BonusDescriptionText{s.Key.ToString()}_{perm.Value}", Parent = "AI_EPIC_BUFF_DESCRIPTION", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0.3 1", AnchorMax = "1 1", OffsetMin = $"0 -{28 + offset}", OffsetMax = $"-10 -{offset}" } } });
                            innerContainer.Add(new CuiLabel { Text = { Text = lang.GetMessage($"{perm.Value}", EpicLoot, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"1 1", OffsetMin = $"3 0", OffsetMax = $"-3 0" } }, $"BonusDescriptionText{s.Key.ToString()}_{perm.Value}", $"BonusDescriptionText_Text{s.Key.ToString()}_{perm.Value}" );

                            offset += 29;
                        }
                    }
                }
                offset+= 2;
            }
            
            offset+= 8;

            // tier rolls

            innerContainer.Add(new CuiElement { Name = $"AI_EPIC_TIERS_HEADER", Parent = "AI_EPIC_BUFF_DESCRIPTION", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.11", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"0 -{20 + offset}", OffsetMax = $"0 -{offset}" } } });
            innerContainer.Add(new CuiLabel { Text = { Text = lang.GetMessage("UI_EPICBUFFDESCRIPTION_TIERS", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"1 1", OffsetMin = $"6 0", OffsetMax = $"-6 0" } }, $"AI_EPIC_TIERS_HEADER", "Header_Text" );

            offset += 23;

            var filteredTiers = set.tierInfo.Where(t => (t.Value.required_crafting_perm == null || permission.UserHasPermission(player.UserIDString, t.Value.required_crafting_perm)));

            foreach (var tierInfo in filteredTiers)
            {
                var col = GetColorFromHtml(epicConfig.tier_information.tier_colours[tierInfo.Key]);
                innerContainer.Add(new CuiElement { Name = $"TierName{tierInfo.Key.ToString()}", Parent = "AI_EPIC_BUFF_DESCRIPTION", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0.3 1", OffsetMin = $"10 -{20 + offset}", OffsetMax = $"0 -{offset}" } } });
                innerContainer.Add(new CuiPanel { Image = { Color = $"{col.r} {col.g} {col.b} {col.a}" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "0 1", OffsetMin = "-10 0", OffsetMax = "0 0" } }, $"TierName{tierInfo.Key.ToString()}", $"TierNameColor{tierInfo.Key.ToString()}" );
                innerContainer.Add(new CuiLabel { Text = { Text = lang.GetMessage(epicConfig.tier_information.tier_display_names[tierInfo.Key], EpicLoot, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = $"1 1 1 1" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"1 1", OffsetMin = $"3 0", OffsetMax = $"-3 0" } }, $"TierName{tierInfo.Key.ToString()}", $"TierNameText{tierInfo.Key.ToString()}" );

                innerContainer.Add(new CuiElement { Name = $"TierRange{tierInfo.Key.ToString()}", Parent = "AI_EPIC_BUFF_DESCRIPTION", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0.3 1", AnchorMax = "1 1", OffsetMin = $"0 -{20 + offset}", OffsetMax = $"-10 -{offset}" } } });
                innerContainer.Add(new CuiLabel { Text = { Text = string.Format("<color=#077E93>min:</color> {0}", $"{tierInfo.Value.min_value * 100}%"), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleRight, Color = "1 1 1 1" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"0.6 1", OffsetMin = $"3 0", OffsetMax = $"-3 0" } }, $"TierRange{tierInfo.Key.ToString()}", $"TierRangeMin{tierInfo.Key.ToString()}" );
                innerContainer.Add(new CuiLabel { Text = { Text = string.Format("<color=#077E93>max:</color> {0}", $"{tierInfo.Value.max_value * 100}%"), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleRight, Color = "1 1 1 1" }, RectTransform = { AnchorMin = $"0.5 0", AnchorMax = $"0.9 1", OffsetMin = $"3 0", OffsetMax = $"-3 0" } }, $"TierRange{tierInfo.Key.ToString()}", $"TierRangeMax{tierInfo.Key.ToString()}" );

                offset += 22;
            }

            offset += 8;

            // tier chances
            innerContainer.Add(new CuiElement { Name = $"AI_EPIC_CHANCES", Parent = "AI_EPIC_BUFF_DESCRIPTION", Components = { new CuiRawImageComponent { Color = "0 0 0 0", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"0 -{offset}", OffsetMax = $"0 -{offset}" } } });

            innerContainer.Add(new CuiElement { Name = $"AI_EPIC_CHANCES_HEADER", Parent = "AI_EPIC_CHANCES", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.11", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"0 -20", OffsetMax = $"0 0" } } });
            innerContainer.Add(new CuiLabel { Text = { Text = lang.GetMessage("UI_EPICBUFFDESCRIPTION_CHANCES", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"1 1", OffsetMin = $"6 0", OffsetMax = $"-6 0" } }, $"AI_EPIC_CHANCES_HEADER", "Header_Text" );

            innerContainer.Add(new CuiElement { Name = "AI_EPIC_CHANCES_BAR_CONTAINER", Parent = "AI_EPIC_CHANCES", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "10 -57", OffsetMax = "-10 -22" } } });
            // container.Add(new CuiLabel { Text = { Text = "Chances", Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.LowerLeft, Color = "0.8 0.8 0.8 1" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"0.15 1", OffsetMin = $"3 3", OffsetMax = $"-3 -3" } }, "AI_EPIC_CHANCES", "AI_EPIC_ROW_CHANCE_BAR_TITLE" );

            innerContainer.Add(new CuiElement { Name = "AI_EPIC_CHANCE_BAR", Parent = "AI_EPIC_CHANCES_BAR_CONTAINER", Components = { new CuiRawImageComponent { Color = "0 0 0 0" }, new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "6 0", OffsetMax = "-6 -3" } } });
            
            float totalWeight = filteredTiers.Sum(s => s.Value.chance_weight);
            float chanceOffset = 0;
            foreach (var tierInfo in filteredTiers)
            {
                var min = chanceOffset;
                var chance = tierInfo.Value.chance_weight/totalWeight;
                var middle = chanceOffset + chance/2;
                var col = GetColorFromHtml(epicConfig.tier_information.tier_colours[tierInfo.Key]);

                innerContainer.Add(new CuiLabel { Text = { Text = $"{Math.Round(chance * 100, 1)}%", Font = "robotocondensed-bold.ttf", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }, RectTransform = { AnchorMin = $"{middle} 1", AnchorMax = $"{middle} 1", OffsetMin = $"-30 -12", OffsetMax = $"32 0" } }, "AI_EPIC_CHANCE_BAR", "TIER_TEXT" );
                innerContainer.Add(new CuiElement { Name = "TIER_SPACER_1", Parent = "AI_EPIC_CHANCE_BAR", Components = { new CuiRawImageComponent { Color = "1 1 1 1", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = $"{middle} 1", AnchorMax = $"{middle} 1", OffsetMin = $"-1 -18", OffsetMax = $"0 -13" } } });
                innerContainer.Add(new CuiElement { Name = "TIER_SPACER_2", Parent = "AI_EPIC_CHANCE_BAR", Components = { new CuiRawImageComponent { Color = "1 1 1 1", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = $"{min} 1", AnchorMax = $"{min + chance} 1", OffsetMin = $"1 -19", OffsetMax = $"-1 -18" } } });
                innerContainer.Add(new CuiElement { Name = "TIER_BAR", Parent = "AI_EPIC_CHANCE_BAR", Components = { new CuiRawImageComponent { Color = $"{col.r} {col.g} {col.b} {col.a}", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = $"{min} 0", AnchorMax = $"{min + chance} 1", OffsetMin = $"1 3", OffsetMax = $"-1 -20" } } });

                chanceOffset += chance;
            }

            offset += 59;

            // scroll panel container

            builder.Add(new CuiElement
            {
                Name = "AI_EPIC_BUFF_DESCRIPTION_SB",
                Parent = EPIC_BUFF_DETAILS_PANEL,
                Components = {
                    new CuiScrollViewComponent {
                        MovementType = UnityEngine.UI.ScrollRect.MovementType.Elastic,
                        Vertical = true,
                        Inertia = true,
                        Horizontal = false,
                        Elasticity = 0.25f,
                        DecelerationRate = 0.3f,
                        ScrollSensitivity = 24f,
                        ContentTransform = new CuiRectTransform { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"0 -{Math.Max(offset, 472)}", OffsetMax = "-5 0" },
                        VerticalScrollbar = new CuiScrollbar() { Size = 2f, AutoHide = true },
                    },
                    new CuiRawImageComponent { Color = "0 0 0 0" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = $"193 {Math.Min(117, (offset-6+32))}", OffsetMax = $"592 589" }
                }
            });

            builder.AddRange(innerContainer);
        }

        // contains buttons to confirm/cancel epic buff selection
        public void AddEpicBuffConfirmPanel(ExtendedCuiElementContainer builder, BasePlayer player, BaseItem item, Buff selectedBuff, bool hasPayment)
        {
            builder.Add(new CuiPanel { Image = { Color = "0.969 0.922 0.882 0.11", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "193 64", OffsetMax = "592 114" } }, EPIC_BUFF_DETAILS_PANEL, "AI_EPIC_BUFF_CONFIRM_PANEL");
            builder.Add(new CuiButton { Button = { Color = hasPayment? "0.45098 0.55294 0.27059 1" : "0.3 0.3 0.3 1", Command = hasPayment ? $"cmdconfirmepicselection {item.uid.Value} {selectedBuff.ToString()}" : null }, Text = { Text = lang.GetMessage("UI_EPICBUFFSELECTION_CONFIRM", this, player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "0.5 1", OffsetMin = "10 5", OffsetMax = "-10 -5" } }, "AI_EPIC_BUFF_CONFIRM_PANEL", "AI_EPIC_BUFF_CONFIRM_BUTTON", "AI_EPIC_BUFF_CONFIRM_BUTTON");
            builder.Add(new CuiButton { Button = { Color = "0.77255 0.23922 0.15686 1", Command = "cmdcancelepicselection" }, Text = { Text = lang.GetMessage("UI_EPICBUFFSELECTION_CANCEL", this, player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }, RectTransform = { AnchorMin = "0.5 0", AnchorMax = "1 1", OffsetMin = "10 5", OffsetMax = "-10 -5" } }, "AI_EPIC_BUFF_CONFIRM_PANEL", "AI_EPIC_BUFF_CANCEL_BUTTON");
        }

        #endregion UIBuilder:Epic

        #region UIBuilder:Perk
        public void AddPerkInfo(ExtendedCuiElementContainer builder, BasePlayer player, List<PerkEntry> perks, string panel, int offset = 50)
        {
            int count = 0;
            foreach(var perk in perks)
            {
                PerkSettings perkMods;
                if (!perkConfig.enhancementSettings.perk_settings.TryGetValue(perk.Perk, out perkMods)) continue;

                // max possible value for the perk
                var perkMax = perkMods.max_mod * 100f;
                // to find perk slider value we have to respect min and max mod values
                var perkSlider = (perk.Value - perkMods.min_mod) / (perkMods.max_mod - perkMods.min_mod);
                // actual value
                // FIXME: retrieve perk values for REGENERATION + suff
                var perkValue = Math.Round(perk.Value * 100, 2);

                builder.Add(new CuiLabel { Text = { Text = lang.GetMessage("UI" + perk.Perk.ToString(), ItemPerks, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleRight, Color = "1 1 1 1" }, RectTransform = { AnchorMin = "0 1", AnchorMax = "0.3 1", OffsetMin = $"3 -{offset + 15 + count * 17}", OffsetMax = $"-3 -{offset + count * 17}" } }, panel, "AI_ITEM_PERK" );
                builder.Add(new CuiElement { Name = "AI_ITEM_PERK_SLIDER", Parent = panel, Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd"}, new CuiRectTransformComponent { AnchorMin = "0.3 1", AnchorMax = "0.9 1", OffsetMin = $"3 -{offset + 15 + count * 17}", OffsetMax = $"3 -{offset + count * 17}" } } });
                builder.Add(new CuiElement { Name = "AI_ITEM_PERK_SLIDE", Parent = "AI_ITEM_PERK_SLIDER", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.11", Sprite = "assets/content/ui/ui.background.tiletex.psd"}, new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = $"{perkSlider} 1", OffsetMin = "0 0", OffsetMax = "0 0" } } });
                builder.Add(new CuiElement { Name = "AI_ITEM_PERK_VALUE", Parent = "AI_ITEM_PERK_SLIDER", Components = { new CuiTextComponent { Text = $"{GetPerkValue(perk.Value, perk.Perk)}{GetPerkTypeString(perk.Perk)}", Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }, new CuiOutlineComponent { Color = "0 0 0 0.5", Distance = "1 -1" }, new CuiRectTransformComponent { AnchorMin = "1 0", AnchorMax = "1 1", OffsetMin = $"-{Math.Max(129 * (1 - perkSlider), 35)} 0", OffsetMax = $"0 0" } } });
                
                builder.Add(new CuiElement { Name = $"PerkDescriptionBtn{perk.Perk.ToString()}", Parent = panel, Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0.9 1", AnchorMax = "0.9 1", OffsetMin = $"4 -{offset + 15 + count * 17}", OffsetMax = $"-21 -{offset + count * 17}" } } });
                builder.Add(new CuiPanel { Image = { Color = "1 1 1 1", Sprite = "assets/icons/info.png" }, RectTransform = { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = $"2 -6", OffsetMax = $"14 6" } }, $"PerkDescriptionBtn{perk.Perk.ToString()}", $"PerkDescriptionBtn{perk.Perk.ToString()}_ICON");
                builder.Add(new CuiButton { Button = { Color = "0 0 0 0", Command = $"cmdshowperkinfopanel {perk.Perk.ToString()}" }, RectTransform = { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "2 -6", OffsetMax = "14 6" } }, $"PerkDescriptionBtn{perk.Perk.ToString()}", $"PerkDescriptionBtn{perk.Perk.ToString()}_BTN");

                count++;
            }
        }

        public void SelectPerkBuffsPanel(ExtendedCuiElementContainer builder, BasePlayer player, string headerText, string bodyText, BaseItem itemToMod, List<Perk> selectedKits, int maxKits, string action, bool hasPayment = true)
        {
            var height = (58 + 226 + (itemToMod.perks.Count * 17) + 12)/2;
            
            var kitItem = new BaseItem(ItemManager.CreateByName(perkConfig.enhancementSettings.enhancement_kit_settings.shortname, 1, perkConfig.enhancementSettings.enhancement_kit_settings.skin));
            var perkmulti = GetPerkMultiplier(selectedKits.Count, action);
            var isWeighted = IsWeightedAction(player, action);
            var requiresKit = isWeighted && RequiresKit(action);

            builder.Add(new CuiElement { Name = SELECT_PERK_BUFF_PANEL, Parent = BACKDROP_PANEL, Components = { new CuiRawImageComponent { Color = "0 0 0 0.95", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat", Sprite = "assets/content/ui/ui.background.gradient.psd" }, new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"0 0", OffsetMax = $"0 0" } }, DestroyUi = SELECT_PERK_BUFF_PANEL });
            builder.Add(new CuiElement { Name = "SELECT_PERK_BUFFS_BACKDROP", Parent = SELECT_PERK_BUFF_PANEL, Components = { new CuiRawImageComponent { Color = "0 0 0 1", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"-202 -{height}", OffsetMax = $"202 {height}" } } });
            
            builder.Add(new CuiElement { Name = $"HeaderPanel", Parent = "SELECT_PERK_BUFFS_BACKDROP", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.11", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"-200 {height-22}", OffsetMax = $"200 {height-2}" } } });
            builder.Add(new CuiLabel { Text = { Text = headerText, Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"1 1", OffsetMin = $"6 0", OffsetMax = $"-6 0" } }, $"HeaderPanel", "HeaderPanel_Text" );
            
            builder.Add(new CuiElement { Name = $"BodyPanel", Parent = "SELECT_PERK_BUFFS_BACKDROP", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"-200 -{height-33}", OffsetMax = $"200 {height-23}" } } });
            builder.Add(new CuiLabel { Text = { Text = bodyText, Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }, RectTransform = { AnchorMin = $"0 1", AnchorMax = $"1 1", OffsetMin = $"6 -36", OffsetMax = $"-6 -6" } }, $"BodyPanel", $"BodyPanel_Text" );
        
            // selected kits
            if (maxKits >= 1)
            {
                builder.Add(new CuiElement { Name = $"Kit1", Parent = "BodyPanel", Components = { new CuiRawImageComponent { Color = selectedKits.Count >= 1 ? "0.45098 0.55294 0.27059 0.55": "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = $"-24 -120", OffsetMax = $"24 -72" } } });
                if (selectedKits.Count >= 1)
                {
                    var kit1 = selectedKits.ElementAt(0);
                    var kitModString = !isWeighted ? "" : $": +{(action == "cmdrandomizeperkvalues" ? "1" : perkConfig.enhancementSettings.perk_settings[kit1].perkWeight * perkmulti)}";
                    builder.Add(new CuiLabel { Text = { Text = $"{lang.GetMessage("UI" + kit1.ToString(), ItemPerks, player.UserIDString)}{kitModString}", Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }, RectTransform = { AnchorMin = $"0.5 1", AnchorMax = $"0.5 1", OffsetMin = $"-70 -70", OffsetMax = $"70 -50" } }, $"BodyPanel", $"Kit1Text" );
                    builder.AddItemIcon(kitItem, 48f, "Kit1", "Kit1_Item");
                    builder.Add(new CuiElement { Name = $"Kit1Line", Parent = "BodyPanel", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = $"-2 -168", OffsetMax = $"2 -120" } } });
                
                    var preservedKits = selectedKits.ToList();
                    preservedKits.RemoveAt(0);
                    builder.Add(new CuiButton { Button = { Color = "0 0 0 0", Command = $"cmdselectperkbuffs {itemToMod.uid.ToString()} {action} {CLI.Serialize(preservedKits)}" }, Text = { Text = " ", Font = "robotocondensed-regular.ttf", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "0 0 0 1" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" } }, "Kit1", "Kit1Cancel");
                }
            }
            
            if (maxKits >= 2)
            {
                builder.Add(new CuiElement { Name = $"Kit2", Parent = "BodyPanel", Components = { new CuiRawImageComponent { Color = selectedKits.Count >= 2 ? "0.45098 0.55294 0.27059 0.55": "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = $"-148 -144", OffsetMax = $"-100 -96" } } });
                if (selectedKits.Count >= 2)
                {
                    var kit2 = selectedKits.ElementAt(1);
                    builder.Add(new CuiLabel { Text = { Text = $"{lang.GetMessage("UI" + kit2.ToString(), ItemPerks, player.UserIDString)}: +{(action == "cmdrandomizeperkvalues" ? "1" : perkConfig.enhancementSettings.perk_settings[kit2].perkWeight * perkmulti)}", Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }, RectTransform = { AnchorMin = $"0.5 1", AnchorMax = $"0.5 1", OffsetMin = $"-194 -94", OffsetMax = $"-54 -74" } }, $"BodyPanel", $"Kit2Text" );
                    builder.AddItemIcon(kitItem, 48f, "Kit2", "Kit2_Item");
                    builder.Add(new CuiElement { Name = $"Kit2Line_1", Parent = "BodyPanel", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = $"-126 -192", OffsetMax = $"-122 -144" } } });
                    builder.Add(new CuiElement { Name = $"Kit2Line_1", Parent = "BodyPanel", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = $"-122 -192", OffsetMax = $"-24 -188" } } });

                    var preservedKits = selectedKits.ToList();
                    preservedKits.RemoveAt(1);
                    builder.Add(new CuiButton { Button = { Color = "0 0 0 0", Command = $"cmdselectperkbuffs {itemToMod.uid.ToString()} {action} {CLI.Serialize(preservedKits)}" }, Text = { Text = " ", Font = "robotocondensed-regular.ttf", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "0 0 0 1" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" } }, "Kit2", "Kit2Cancel");
                }
            }
            
            if (maxKits == 3)
            {
                builder.Add(new CuiElement { Name = $"Kit3", Parent = "BodyPanel", Components = { new CuiRawImageComponent { Color = selectedKits.Count >= 3 ? "0.45098 0.55294 0.27059 0.55": "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = $"100 -144", OffsetMax = $"148 -96" } } });
                if (selectedKits.Count >= 3)
                {
                    var kit3 = selectedKits.ElementAt(2);
                    builder.Add(new CuiLabel { Text = { Text = $"{lang.GetMessage("UI" + kit3.ToString(), ItemPerks, player.UserIDString)}: +{(action == "cmdrandomizeperkvalues" ? "1" : perkConfig.enhancementSettings.perk_settings[kit3].perkWeight * perkmulti)}", Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }, RectTransform = { AnchorMin = $"0.5 1", AnchorMax = $"0.5 1", OffsetMin = $"54 -94", OffsetMax = $"194 -74" } }, $"BodyPanel", $"Kit3Text" );
                    builder.AddItemIcon(kitItem, 48f, "Kit3", "Kit3_Item");
                    builder.Add(new CuiElement { Name = $"Kit3Line_1", Parent = "BodyPanel", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = $"122 -192", OffsetMax = $"126 -144" } } });
                    builder.Add(new CuiElement { Name = $"Kit3Line_1", Parent = "BodyPanel", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = $"24 -192", OffsetMax = $"122 -188" } } });
                
                    var preservedKits = selectedKits.ToList();
                    preservedKits.RemoveAt(2);
                    builder.Add(new CuiButton { Button = { Color = "0 0 0 0", Command = $"cmdselectperkbuffs {itemToMod.uid.ToString()} {action} {CLI.Serialize(preservedKits)}" }, Text = { Text = " ", Font = "robotocondensed-regular.ttf", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "0 0 0 1" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" } }, "Kit3", "Kit3Cancel");
                }
            }
                
            // selected item
            builder.Add(new CuiElement { Name = $"SelectedItem", Parent = "BodyPanel", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.22", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = $"-24 -216", OffsetMax = $"24 -168" } } });
            builder.AddItemIcon(itemToMod, 48f, "SelectedItem", "SelectedItemIcon");

            // perk info
            builder.Add(new CuiElement { Name = $"ItemPerks", Parent = "BodyPanel", Components = { new CuiRawImageComponent { Color = "0 0 0 0", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = $"-110 -{226 + (itemToMod.perks.Count * 17)}", OffsetMax = $"100 -226" } } });
            AddPerkInfo(builder, player, itemToMod.perks, "ItemPerks", 0);

            // footer
            builder.Add(new CuiPanel { Image = { Color = "0.969 0.922 0.882 0.11", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"-200 -{height-2}", OffsetMax = $"200 -{height-32}" } }, "SELECT_PERK_BUFFS_BACKDROP", "ActionsPanel");
            builder.Add(new CuiButton { Button = { Color = hasPayment && (isWeighted || selectedKits.Count == maxKits) && (!requiresKit || selectedKits.Count > 0) ? "0.45098 0.55294 0.27059 1" : "0.3 0.3 0.3 1", Command = hasPayment && (isWeighted || selectedKits.Count == maxKits) && (!requiresKit || selectedKits.Count > 0) ? $"{action} {itemToMod.uid.ToString()} {CLI.Serialize(selectedKits)}" : " " }, Text = { Text = lang.GetMessage("UI_PERKBUFFSELECTION_CONFIRM", this, player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "0.5 1", OffsetMin = "10 5", OffsetMax = "-10 -5" } }, "ActionsPanel", "ConfirmButton", "ConfirmButton");
            builder.Add(new CuiButton { Button = { Color = "0.77255 0.23922 0.15686 1", Command = $"cmdcloseselectperkbuffs" }, Text = { Text = lang.GetMessage("UI_PERKBUFFSELECTION_CANCEL", this, player.UserIDString), Font = "robotocondensed-regular.ttf", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }, RectTransform = { AnchorMin = "0.5 0", AnchorMax = "1 1", OffsetMin = "10 5", OffsetMax = "-10 -5" } }, "ActionsPanel", "CloseButton");
        }

        public void AdditionalCostPanel(ExtendedCuiElementContainer builder, BasePlayer player, string parent, CraftItem craftItem, int additionalCost, bool hasPayment = true)
        {
            builder.Add(new CuiElement { Name = "ADDITIONAL_COST_BACKDROP", Parent = parent, Components = { new CuiRawImageComponent { Color = "0 0 0 1", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"-410 43", OffsetMax = $"-206 150" } } });
            
            builder.Add(new CuiElement { Name = $"AdditionalCostHeaderPanel", Parent = "ADDITIONAL_COST_BACKDROP", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.11", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = $"-100 -22", OffsetMax = $"100 -2" } } });
            builder.Add(new CuiLabel { Text = { Text = lang.GetMessage("UI_ADDITIONAL_COST", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"1 1", OffsetMin = $"6 0", OffsetMax = $"-6 0" } }, $"AdditionalCostHeaderPanel", "AdditionalCostHeaderPanel_Text" );

            builder.Add(new CuiElement { Name = $"AdditionalCostBodyPanel", Parent = "ADDITIONAL_COST_BACKDROP", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = $"-100 -105", OffsetMax = $"100 -23" } } });
            
            builder.Add(new CuiLabel { Text = { Text = $"{craftItem.display_name.ToUpper()}", Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }, RectTransform = { AnchorMin = $"0.5 1", AnchorMax = $"0.5 1", OffsetMin = $"-80 -24", OffsetMax = $"80 -4" } }, $"AdditionalCostBodyPanel", $"CurrencyName" );
            builder.Add(new CuiElement { Name = $"Currency", Parent = "AdditionalCostBodyPanel", Components = { new CuiRawImageComponent { Color = hasPayment ? "0.45098 0.55294 0.27059 0.55" : "0.969 0.922 0.882 0.22", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = $"-24 -72", OffsetMax = $"24 -24" } } });
            
            var currency = ItemManager.CreateByName(craftItem.shortname, additionalCost, craftItem.skin);
            builder.AddItemIconWithAmount(new BaseItem(currency), 48f, "Currency", "CurrencyIcon");
        }
        
        public void SelectKitPanel(ExtendedCuiElementContainer builder, BasePlayer player, string parent, int height, BaseItem itemToMod, List<Perk> selectedKits, int maxKits, string action)
        {
            builder.Add(new CuiElement { Name = "SELECT_KIT_BACKDROP", Parent = parent, Components = { new CuiRawImageComponent { Color = "0 0 0 0", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"206 -{height}", OffsetMax = $"410 {height}" } } });
            
            builder.Add(new CuiElement { Name = $"SelectKitHeaderPanel", Parent = "SELECT_KIT_BACKDROP", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.11", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"-100 {height-22}", OffsetMax = $"100 {height-2}" } } });
            builder.Add(new CuiLabel { Text = { Text = lang.GetMessage("UI_KITSELECTION", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"1 1", OffsetMin = $"6 0", OffsetMax = $"-6 0" } }, $"SelectKitHeaderPanel", "SelectKitHeaderPanel_Text" );
            
            // builder.Add(new CuiElement { Name = $"SelectKitBodyPanel", Parent = "SELECT_KIT_BACKDROP", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"-200 -{height-33}", OffsetMax = $"200 {height-23}" } } });
            
            var availableKits = getAvailableKits(player);
            var itemPerks = itemToMod.perks.Select(el => el.Perk).ToList();

            foreach (var selected in selectedKits)
                availableKits[selected]--;

            ExtendedCuiElementContainer innerContainer = new ExtendedCuiElementContainer();
            
            var offset = 0;

            if (availableKits.Count == 0)
            {
                innerContainer.Add(new CuiElement { Name = $"SelectKitNone", Parent = "SELECT_KIT_SB", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"10 -{33 + offset}", OffsetMax = $"0 -{offset}" } } });
                innerContainer.Add(new CuiLabel { Text = { Text = lang.GetMessage("UI_KITSELECTION_NONE", this, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 0.3" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"1 1", OffsetMin = $"3 0", OffsetMax = $"-3 0" } }, $"SelectKitNone", $"SelectKitNone_Text" );

                offset += 34;
            }

            // foreach(var kit in Enum.GetValues(typeof(Perk)).Cast<Perk>())
            foreach(var kit in availableKits.Keys)
            {
                PerkSettings perkSettings;
                // FIXME: we need to check whitelist/blacklist of the item we want to craft on
                if (!perkConfig.enhancementSettings.perk_settings.TryGetValue(kit, out perkSettings) || !perkSettings.enabled) continue;

                var kitSelection = selectedKits.ToList();
                kitSelection.Add(kit);

                var hasPerk = itemPerks.Contains(kit);
                var canRandomizePerk = action == "cmdrandomizeperkvalues" && hasPerk && itemPerks.Where(i => (i == kit)).Count() > selectedKits.Where(i => (i == kit)).Count();
                var canRemovePerk = action == "cmdremoveperk" && hasPerk;
                var canAddPerk = action == "cmdaddperk" && (!itemPerks.Contains(kit) || config.craft_settings.add_perk_settings.duplicates);
                var canAddKit = availableKits.ContainsKey(kit) && availableKits[kit] > 0 && selectedKits.Count < maxKits && (canAddPerk || canRemovePerk || canRandomizePerk);

                innerContainer.Add(new CuiElement { Name = $"KitAddButton{kit.ToString()}", Parent = "SELECT_KIT_SB", Components = { new CuiRawImageComponent { Color = canAddKit ? "0.45098 0.55294 0.27059 1" : "0.3 0.3 0.3 1", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"0 -{20 + offset}", OffsetMax = $"20 -{offset}" } } });
                innerContainer.Add(new CuiPanel { Image = { Color = "1 1 1 1", Sprite = "assets/icons/add.png" }, RectTransform = { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = $"4 -6", OffsetMax = $"16 6" } }, $"KitAddButton{kit.ToString()}", $"KitAddButton{kit.ToString()}_ICON");
                innerContainer.Add(new CuiButton { Button = { Color = "0 0 0 0", Command = canAddKit ? $"cmdselectperkbuffs {itemToMod.uid.ToString()} {action} {CLI.Serialize(kitSelection)}" : " " }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" } }, $"KitAddButton{kit.ToString()}", $"KitAddButton{kit.ToString()}_BTN");

                innerContainer.Add(new CuiElement { Name = $"KitName{kit.ToString()}", Parent = "SELECT_KIT_SB", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"20 -{20 + offset}", OffsetMax = $"0 -{offset}" } } });
                innerContainer.Add(new CuiLabel { Text = { Text = lang.GetMessage("UI" + kit.ToString(), ItemPerks, player.UserIDString), Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = $"1 1 1 1" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"0.7 1", OffsetMin = $"3 0", OffsetMax = $"-3 0" } }, $"KitName{kit.ToString()}", $"KitNameText{kit.ToString()}" );
                innerContainer.Add(new CuiLabel { Text = { Text = availableKits.ContainsKey(kit) ? $"x{availableKits[kit]}" : "x0", Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleRight, Color = $"1 1 1 1" }, RectTransform = { AnchorMin = $"0.7 0", AnchorMax = $"1 1", OffsetMin = $"3 0", OffsetMax = $"-23 0" } }, $"KitName{kit.ToString()}", $"KitAmountText{kit.ToString()}" );

                innerContainer.Add(new CuiElement { Name = $"KitInfoButton{kit.ToString()}", Parent = "SELECT_KIT_SB", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = $"-20 -{20 + offset}", OffsetMax = $"0 -{offset}" } } });
                innerContainer.Add(new CuiPanel { Image = { Color = "1 1 1 1", Sprite = "assets/icons/info.png" }, RectTransform = { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = $"4 -6", OffsetMax = $"16 6" } }, $"KitInfoButton{kit.ToString()}", $"KitInfoButton{kit.ToString()}_ICON");
                innerContainer.Add(new CuiButton { Button = { Color = "0 0 0 0", Command = $"cmdshowperkinfopanel {kit.ToString()}" }, RectTransform = { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = "2 -6", OffsetMax = "14 6" } }, $"KitInfoButton{kit.ToString()}", $"KitInfoButton{kit.ToString()}_BTN");
                offset += 22;
            }

            // scroll panel container

            builder.Add(new CuiElement
            {
                Name = "SELECT_KIT_SB",
                Parent = "SELECT_KIT_BACKDROP",
                Components = {
                    new CuiScrollViewComponent {
                        MovementType = UnityEngine.UI.ScrollRect.MovementType.Elastic,
                        Vertical = true,
                        Inertia = true,
                        Horizontal = false,
                        Elasticity = 0.25f,
                        DecelerationRate = 0.3f,
                        ScrollSensitivity = 24f,
                        ContentTransform = new CuiRectTransform { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = $"0 -{offset}", OffsetMax = "-5 0" },
                        VerticalScrollbar = new CuiScrollbar() { Size = 2f, AutoHide = true },
                    },
                    new CuiRawImageComponent { Color = "0 0 0 0" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = $"-100 -{Math.Min(height*2-23, (offset-6+32))}", OffsetMax = $"100 -23" }
                    // new CuiRectTransformComponent { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = $"-587 {Math.Min(225, (offset-6+32))}", OffsetMax = $"-213 589" }
                }
            });

            builder.AddRange(innerContainer);
        }

        #endregion UIBilder:Perk

        #region UIBuilder:Component Names

        static string COMMAND_SELECT_ITEM = "cmdselectitem";
        static string CLOSE_COMMAND = "cmdcloseinventory";

        static string MAIN_BUTTON = "AI_MAIN_BUTTON";

        static string BACKDROP_PANEL = "AI_BACKDROP_PANEL";
        static string CLOSE_BUTTON = "closebutton";

        static string AI_INFO_BOX = "AI_INFO_BOX";
        
        static string INVENTORY_TITLE = "AI_INVENTORY_TITLE";
        static string INVENTORY_PANEL = "AI_INVENTORY_ITEM_CONTAINER";
        static string INVENTORY_ITEM_SLOT = "INVENTORY_ITEM";

        static string BELT_PANEL = "AI_BELT_ITEM_CONTAINER";
        static string BELT_ITEM_SLOT = "BELT_ITEM";

        static string WEAR_PANEL = "AI_WEAR_ITEM_CONTAINER";
        static string WEAR_ITEM_SLOT = "WEAR_ITEM";

        static string ITEM_WRAPPER = "ITEM_WRAPPER";
        static string ITEM_CONTAINER = "ITEM";

        static string ITEM_DETAILS_CONTAINER = "ITEM_DETAILS_CONTAINER";
        static string ITEM_ACTIONS_CONTAINER = "AI_ITEM_ACTIONS";

        static string EPIC_BUFF_DETAILS_PANEL = "EPIC_BUFF_DETAILS_PANEL";

        static string PLAYER_BUFFS_PANEL = "PLAYER_BUFFS_PANEL";

        static string SELECT_PERK_BUFF_PANEL = "SELECT_PERK_BUFF_PANEL";

        #endregion UIBuilder:Component Names

        /**
        public static class RustColor
        {
            public static UiColor Blue = new UiColor(0.08627f, 0.25490f, 0.38431f, 1);
            public static UiColor LightBlue = new UiColor(0.25490f, 0.61176f, 0.86275f, 1);
            //public static UiColor Red = new UiColor(0.68627 0.21569 0.1f4118 1);
            public static UiColor Red = new UiColor(0.77255f, 0.23922f, 0.15686f, 1);
            public static UiColor Maroon = new UiColor(0.46667f, 0.22745f, 0.18431f, 1);
            public static UiColor LightMaroon = new UiColor(1.00000f, 0.32549f, 0.21961f, 1);
            //public static UiColor LightRed = new UiColor(0.9f, 1373f, 0.77647f, 0.75686f, 1);
            //public static UiColor Green = new UiColor(0.25490, 0.30980, 0.1f4510, 1);
            public static UiColor Green = new UiColor(0.35490f, 0.40980f, 0.24510f, 1);
            public static UiColor LightGreen = new UiColor(0.76078f, 0.94510f, 0.41176f, 1);
            public static UiColor Gray = new UiColor(0.45490f, 0.43529f, 0.40784f, 1);
            public static UiColor LightGray = new UiColor(0.69804f, 0.66667f, 0.63529f, 1);
            public static UiColor Orange = new UiColor(1, 0.53333f, 0.18039f, 1);
            public static UiColor LightOrange = new UiColor(1, 0.82353f, 0.44706f, 1);
            public static UiColor White = new UiColor(0.87451f, 0.83529f, 0.8f, 1);
            public static UiColor LightWhite = new UiColor(0.97647f, 0.97647f, 0.97647f, 1);
            public static UiColor Lime = new UiColor(0.64706f, 1, 0, 1);
            public static UiColor LightLime = new UiColor(0.69804f, 0.83137f, 0.46667f, 1);
            public static UiColor DarkGray = new UiColor(0.08627f, 0.08627f, 0.08627f, 1);
            public static UiColor DarkBrown = new UiColor(0.15686f, 0.15686f, 0.12549f, 1);
            public static UiColor LightBrown = new UiColor(0.54509f, 0.51372f, 0.4705f, 1);
        }
        **/

        #endregion

        #region UIComponents
        
        // add all special reusable components here
        public class ExtendedCuiElementContainer : CuiElementContainer
        {
            public void AddItemButton(BaseItem Item, float Size, string Command, string Parent, string Name)
            {
                var imageSize = Size / 1.2f;
                var imageOffset = (Size - imageSize) / 2f;

                AddItemIcon(Item, Size, Parent, Name);

                if (Item.hasCondition)
                {
                    Add(new CuiPanel {
                        Image = { Color = "0.55 0.78 0.24 0.25", Sprite = "assets/content/ui/ui.background.tiletex.psd" },
                        RectTransform = { AnchorMin = "0 0", AnchorMax = "0 1", OffsetMax = $"{imageOffset} 0" }
                    }, Name, $"{Name}_ConditionBG");

                    Add(new CuiPanel {
                        Image = { Color = "0.55 0.78 0.24 0.75", Sprite = "assets/content/ui/ui.background.tiletex.psd" },
                        RectTransform = { AnchorMin = "0 0", AnchorMax = $"0 {Item.condition}", OffsetMax = $"{imageOffset} 0" }
                    }, $"{Name}_ConditionBG", $"{Name}_Condition");

                    Add(new CuiPanel {
                        Image = { Color = "1 1 1 1", Sprite = "assets/content/ui/ui.background.tiletex.psd" },
                        RectTransform = { AnchorMin = $"0 {Item.maxCondition}", AnchorMax = $"0 1", OffsetMax = $"{imageOffset} 0" }
                    }, $"{Name}_ConditionBG", $"{Name}_ConditionMax");
                }

                // FIXME: ammocount is shown behind icon
                if (Item.ammoCount != null)
                    Add(new CuiLabel {
                        Text = { Text = $"{FormattableString.Invariant($"{Item.ammoCount:N0}")}", Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.LowerRight, Color = "1 1 1 0.3" },
                        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "-10 0", OffsetMax = $"-3 0" }
                    }, Name, "AmmoCount");

                if (Item.amount > 1)
                    Add(new CuiLabel {
                        Text = { Text = $"x{FormattableString.Invariant($"{Item.amount:N0}")}", Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.LowerRight, Color = "1 1 1 0.3" },
                        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "-10 0", OffsetMax = $"-3 0" }
                    }, Name, "Amount");

                Add(new CuiButton {
                    Button = { Color = "0 0 0 0", Command = Command },
                    Text = { Text = " ", Font = "robotocondensed-regular.ttf", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "0 0 0 1" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                }, Name, "BUTTON");
            }

            public void AddItemIcon(BaseItem Item, float Size, string Parent, string Name)
            {
                var imageSize = Size / 1.2f;
                var imageOffset = (Size - imageSize) / 2f;

                Add(new CuiPanel
                {
                    Image = { Color = "0 0 0 0" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" },
                }, Parent, Name, Name);

                // background for epic items
                if (Item.buff != null)
                {
                    var col = GetColorFromHtml(Instance.epicConfig.tier_information.tier_colours[Item.buff.Tier]);
                    var offset = imageOffset * 1.5f;

                    Add(new CuiElement
                    {
                        Components = {
                            new CuiRawImageComponent { Color = $"{col.r} {col.g} {col.b} {col.a}", Sprite = "assets/content/ui/tiledpatterns/swirl_pattern.png" },
                            new CuiOutlineComponent { Color = "0.2641509 0.2641509 0.2641509 1", Distance = $"{offset / 1.5f} {-offset / 1.5f}" },
                            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"{offset} {offset}", OffsetMax = $"{-offset} {-offset}" }
                        },
                        Parent = Name,
                        Name = "EpicOutline",
                    });
                    Add(new CuiElement
                    {
                        Components = {
                            new CuiRawImageComponent { Color = $"{col.r} {col.g} {col.b} 0.5", Sprite = "assets/content/ui/tiledpatterns/stripe_thin.png" },
                            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"{offset} {offset}", OffsetMax = $"{-offset} {-offset}" }
                        },
                        Parent = Name,
                        Name = "EpicBg",
                    });
                }

                Add(new CuiPanel {
                    Image = { ItemId = Item.itemid, SkinId = Item.skin },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"{imageOffset} {imageOffset * 1.5}", OffsetMax = $"{-imageOffset} {-imageOffset * 0.5}" }
                }, Name, "Icon");

                if (Item.perks.Count > 0)
                {
                    Add(new CuiElement
                    {
                        Components = {
                            new CuiRawImageComponent { Color = "0.2641509 0.2641509 0.2641509 1", Sprite = "assets/icons/star.png" },
                            new CuiRectTransformComponent { AnchorMin = "1 0", AnchorMax = "1 0", OffsetMin = $"-24 4", OffsetMax = $"-4 24" }
                        },
                        Parent = Name,
                        Name = "PerkIcon",
                    });

                    Add(new CuiElement
                    {
                        Components = {
                            new CuiTextComponent { Text = $"{Item.perks.Count}", Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                            new CuiOutlineComponent { Color = "0 0 0 1", Distance = "1 -1" },
                            new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-10 -10", OffsetMax = "10 10" }
                        },
                        Parent = "PerkIcon",
                        Name = "PerkLabel",
                    });
                }
            }

            public void AddItemIconWithAmount(BaseItem Item, float Size, string Parent, string Name)
            {
                AddItemIcon(Item, Size, Parent, Name);

                if (Item.amount > 1)
                    Add(new CuiLabel {
                        Text = { Text = $"x{FormattableString.Invariant($"{Item.amount:N0}")}", Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.LowerRight, Color = "1 1 1 1" },
                        RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "-10 0", OffsetMax = $"-3 0" }
                    }, Name, "Amount");
            }

            public void AddActionButton(string Action, int Offset, string Sprite, string Command, string Parent, string Name)
            {
                Add(new CuiElement { Name = Name, Parent = Parent, Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.11", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 0", OffsetMin = $"0 {Offset}", OffsetMax = $"0 {32 + Offset}" } } });
                Add(new CuiPanel { Image = { Color = "1 1 1 1", Sprite = Sprite }, RectTransform = { AnchorMin = "0 0.5", AnchorMax = "0 0.5", OffsetMin = $"6 -10", OffsetMax = $"26 10" } }, $"{Name}", $"{Name}_ICON");
                Add(new CuiLabel { Text = { Text = $"{Action}", Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"32 0", OffsetMax = $"-3 0" } }, $"{Name}", $"{Name}_TEXT");
                Add(new CuiButton { Button = { Color = "0 0 0 0", Command = Command }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "0 0" } }, $"{Name}", $"{Name}_BTN");
            }

            public void AddInfoBox(string headerText, string bodyText, string buttonText, int bodyHeight = 74)
            {
                var height = (58 + bodyHeight)/2;
                Add(new CuiElement { Name = "AI_INFO_BOX", Parent = BACKDROP_PANEL, Components = { new CuiRawImageComponent { Color = "0 0 0 0.95", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat", Sprite = "assets/content/ui/ui.background.gradient.psd" }, new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"0 0", OffsetMax = $"0 0" } } });
                Add(new CuiElement { Name = "AI_INFO_BOX_BACKDROP", Parent = "AI_INFO_BOX", Components = { new CuiRawImageComponent { Color = "0 0 0 1", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"-142 -{height}", OffsetMax = $"142 {height}" } } });
            
                Add(new CuiElement { Name = $"HeaderInfoPanel", Parent = "AI_INFO_BOX_BACKDROP", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.11", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"-140 {height-22}", OffsetMax = $"140 {height-2}" } } });
                Add(new CuiLabel { Text = { Text = headerText, Font = "robotocondensed-bold.ttf", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"1 1", OffsetMin = $"6 0", OffsetMax = $"-6 0" } }, $"HeaderInfoPanel", "HeaderInfoPanel_Text" );
            
                Add(new CuiElement { Name = $"BodyInfoPanel", Parent = "AI_INFO_BOX_BACKDROP", Components = { new CuiRawImageComponent { Color = "0.969 0.922 0.882 0.055", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"-140 -{height-33}", OffsetMax = $"140 {height-23}" } } });
                Add(new CuiLabel { Text = { Text = bodyText, Font = "robotocondensed-bold.ttf", FontSize = 12, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"1 1", OffsetMin = $"6 0", OffsetMax = $"-6 0" } }, $"BodyInfoPanel", $"BodyInfoPanel_Text" );
        
                Add(new CuiPanel { Image = { Color = "0.969 0.922 0.882 0.11", Sprite = "assets/content/ui/ui.background.tiletex.psd" }, RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"-140 -{height-2}", OffsetMax = $"140 -{height-32}" } }, "AI_INFO_BOX_BACKDROP", "ActionsInfoPanel");
                Add(new CuiButton { Button = { Color = "0.45098 0.55294 0.27059 1", Command = $"cmdcloseinfobox" }, Text = { Text = buttonText, Font = "robotocondensed-regular.ttf", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }, RectTransform = { AnchorMin = "0.2 0", AnchorMax = "0.8 1", OffsetMin = "10 5", OffsetMax = "-10 -5" } }, "ActionsInfoPanel", "CloseInfoPanelButton");
            }
        }

        static Color GetColorFromHtml(string color)
        {
            return ColorUtility.TryParseHtmlString(color, out Color col) ? col : Color.black;
        }
        #endregion

        #region Config
        private Configuration config;

        #region Plugin Config

        public class Configuration
        {
            [JsonProperty("The chat command to open the UI (null = none)")]
            public string chatCmd = "aicrafting";

            [JsonProperty("Advanced Craft Settings")]
            public AdvancedCraftSettings craft_settings = new AdvancedCraftSettings();
            
            [JsonProperty("UI Custom Button")]
            public CustomButton customButton = new CustomButton();
        }

        public class CustomButton
        {
            [JsonProperty("Should show a custom button on the Hud? (default = false)")]
            public bool enabled = false;
            [JsonProperty("Icon shown on the button")]
            public string Icon = "assets/icons/inventory.png";
            [JsonProperty("Should we show that button on the HUD or as an Overlay?")]
            public string Parent = "Overlay";
            [JsonProperty("Brackground Color")]
            public string BackgroundColor = "0.969 0.922 0.882 0.15";
            [JsonProperty("Anchor Min")]
            public string AnchorMin = "0.5 0";
            [JsonProperty("Anchor Max")]
            public string AnchorMax = "0.5 0";
            [JsonProperty("Offset Min")]
            public string OffsetMin = "-263 18";
            [JsonProperty("Offset Max")]
            public string OffsetMax = "-204 78";
        }

        public class AdvancedCraftSettings
        {
            [JsonProperty("Add Perk")]
            public AddPerkSettings add_perk_settings = new AddPerkSettings();

            [JsonProperty("Remove Perk")]
            public RemovePerkSettings remove_perk_settings = new RemovePerkSettings();

            [JsonProperty("Radomize Perk Values")]
            public RandomizePerkValuesSettings randomize_perk_settings = new RandomizePerkValuesSettings();

            [JsonProperty("Upgrade Perk Tier")]
            public UpgradePerkTierConfig upgrade_perk_settings = new UpgradePerkTierConfig();
        }

        public class AddPerkSettings
        {
            [JsonProperty("enabled")]
            public bool enabled = true;

            [JsonProperty("Can add perks to Epic Items (default = false)")]
            public bool perksForEpic = false;

            [JsonProperty("Can add perks to items without perks (default = false)")]
            public bool perksForBlankItems = false;

            [JsonProperty("Can add the same mod multiple times (default = false)")]
            public bool duplicates = false;

            [JsonProperty("The number of perks a player can craft on items (default = 3)")]
            public int maxPossiblePerks = 3;

            [JsonProperty("Item to use when adding a perk")]
            public CraftItem craft_item = new CraftItem();

            [JsonProperty("Use weighting to select a perk")]
            public WeightRolls weight_system = new WeightRolls();
        }

        public class WeightRolls
        {
            [JsonProperty("enabled (player can select the perk directly if disabled)")]
            public bool enabled = true;

            [JsonProperty("Multiplier to use if 1 Kit is used")]
            public float multiplier_1 = 15f;

            [JsonProperty("Multiplier to use if 2 Kits are used")]
            public float multiplier_2 = 20f;

            [JsonProperty("Multiplier to use if 3 Kits are used")]
            public float multiplier_3 = 40f;

            [JsonProperty("Requires at least 1 Kit (default = true)")]
            public bool requires_kit = true;

            [JsonProperty("Effect played when kit mod was selected")]
            public string success_effect = "assets/prefabs/misc/halloween/lootbag/effects/gold_open.prefab";

            [JsonProperty("Effect played when a random mod was selected")]
            public string fail_effect = "assets/prefabs/deployable/bear trap/effects/bear-trap-deploy.prefab";
        }

        public class RemovePerkSettings
        {
            [JsonProperty("enabled")]
            public bool enabled = true;

            [JsonProperty("Can remove ALL perks from an item to make it normal (default = false)")]
            public bool canRemoveAllPerks = false;

            [JsonProperty("Item to use when removing a perk")]
            public CraftItem craft_item = new CraftItem();

            [JsonProperty("Use weighting to select a perk")]
            public WeightRolls weight_system = new WeightRolls();
        }

        public class RandomizePerkValuesSettings
        {
            [JsonProperty("enabled")]
            public bool enabled = true;

            [JsonProperty("Item to use when randomizing perk values")]
            public CraftItem randomize_perk_item = new CraftItem();

            [JsonProperty("Use kits to allow lucky rolls")]
            public bool allow_lucky_rolls = true;

            [JsonProperty("Requires at least 1 Kit (default = true)")]
            public bool requires_kit = true;

            [JsonProperty("Effect played when rolling the item is done")]
            public string success_effect = "assets/prefabs/deployable/research table/effects/research-success.prefab";
        }

        public class UpgradePerkTierConfig
        {
            [JsonProperty("Number of perk tiers. If this is 1, upgrading an item is not possible. (default = 3)")]
            public int perk_tiers = 3;

            [JsonProperty("Chance that upgrading can fail (default = 30%)")]
            public float upgrade_fail_chance = 0.3f;

            [JsonProperty("Chance that perks will downgrade if upgrade attempt failed (default = 25%)")]
            public float downgrade_chance = 0.25f;

            [JsonProperty("Item to use when upgrading a perk")]
            public CraftItem craft_item = new CraftItem();

            [JsonProperty("Use weighting to select a perk")]
            public WeightRolls weight_system = new WeightRolls();
        }

        public class CraftItem
        {
            public string display_name = "epic scrap";
            public string shortname = "blood";
            public ulong skin = 2834920066;
            public int amount = 100;

            [JsonProperty("Additional cost per Kit")]
            public Dictionary<Perk, int> cost_per_kit = new Dictionary<Perk, int>();
        }
        #endregion


        private EpicLootConfiguration epicConfig;

        /**
         * Definitions of required configs for epic loot to support display of buff infos and set piece bonusses
         * All required data is loaded form epic loot config file
         * 
         * upgrades will be handled by the origin plugin
         * recycling will be handled by the origin plugin
         **/
        #region Epic Loot Config

        public class EpicLootConfiguration
        {
            [JsonProperty("Tier information")]
            public TierSettings tier_information = new TierSettings();

            [JsonProperty("Enhancement information")]
            public Dictionary<Buff, EnhancementInfo> enhancements = new Dictionary<Buff, EnhancementInfo>();

            [JsonProperty("Scrapper settings")]
            public ScrapperSettings scrapper_settings = new ScrapperSettings();

            [JsonProperty("List of items that can not be enhanced")]
            public List<string> global_blacklist = new List<string>();

            [JsonProperty("List of skin IDs that cannot be enhanced")]
            public List<ulong> skin_blacklist = new List<ulong>();
        }

        public class TierSettings
        {
            [JsonProperty("Tier display names that show up in the menu")]
            public Dictionary<string, string> tier_display_names = new Dictionary<string, string>()
            {
                ["s"] = "Legendary",
                ["a"] = "Epic",
                ["b"] = "Rare",
                ["c"] = "Uncommon"
            };

            [JsonProperty("Tier colours")]
            public Dictionary<string, string> tier_colours = new Dictionary<string, string>()
            {
                ["s"] = "#FFF900",
                ["a"] = "#9500BA",
                ["b"] = "#077E93",
                ["c"] = "#AE5403"
            };

        }

        public class EnhancementInfo
        {
            public bool enabled;
            // Black list will prevent matching items from being enchanted with this buff type.
            public List<string> item_blacklist;

            // If Whitelist is not null, it will only allow these types of items.
            public List<string> item_whitelist;

            public Dictionary<string, TierInfo> tierInfo;

            public Dictionary<int, SetBonusEffect> setInfo;

            public class TierInfo
            {
                public int chance_weight;
                public float min_value;
                public float max_value;
                // FIXME: this should be added, even when i dont like it
                public string required_crafting_perm;
                public TierInfo(int chance_weight, float min_value, float max_value, string req_craft_perm = null)
                {
                    this.chance_weight = chance_weight;
                    this.min_value = min_value;
                    this.max_value = max_value;
                    this.required_crafting_perm = req_craft_perm;
                }
            }

            public EnhancementInfo(bool enabled, Dictionary<string, TierInfo> tierInfo, Dictionary<int, SetBonusEffect> setInfo)
            {
                this.enabled = enabled;
                this.tierInfo = tierInfo;
                this.setInfo = setInfo;
            }
        }

        public class SetBonusEffect
        {
            public Dictionary<Buff, SetBonusValues> setBonus;
            public SetBonusEffect(Dictionary<Buff, SetBonusValues> setBonus)
            {
                this.setBonus = setBonus;
            }
        }

        public class SetBonusValues
        {
            public float modifier;

            [JsonProperty("Permissions [permission / title]")]
            public Dictionary<string, string> perms = new Dictionary<string, string>();

            public SetBonusValues(float modifier, Dictionary<string, string> perms = null)
            {
                this.modifier = modifier;
                this.perms = perms;
            }
        }

        public class ScrapperSettings
        {
            [JsonProperty("Enable scrapping of equipment for a special currency that can be used to enhanced weapons?")]
            public bool enabled = true;

            [JsonProperty("Name of the scrapper currency")]
            public string currency_name = "epic scrap";

            [JsonProperty("Shortname of the item the currency will be based off of")]
            public string currency_shortname = "blood";

            [JsonProperty("Currency skin")]
            public ulong currency_skin = 2834920066;

            [JsonProperty("Currency received for scrapping items based on tier")]
            public Dictionary<string, int> scrapper_value = new Dictionary<string, int>();

            [JsonProperty("Cost to enhance an item based on the buff type")]
            public Dictionary<Buff, int> enhancement_cost = new Dictionary<Buff, int>();
        }

        #endregion


        private ItemPerksConfiguration perkConfig;

        #region Item Perk Config

        public class ItemPerksConfiguration
        {
            [JsonProperty("Enhancement Settings")]
            public EnhancementSettings enhancementSettings = new EnhancementSettings();
        }

        public class EnhancementSettings
        {
            [JsonProperty("Loot settings")]
            public LootSettings lootSettings = new LootSettings();

            [JsonProperty("Chance for an item to receive additional perks after successfully rolling its first perk? [out of 100]")]
            public Dictionary<int, float> additional_perk_chances;

            // [JsonProperty("Allow items to have the same perk more than once?")]
            // public bool allow_duplicate_perks = true;

            // [JsonProperty("Consider duplicate buffs when working out how many existing buffs an item has? [false means an item can have infinite duplicate buffs]")]
            // public bool add_duplicates_to_buff_count = true;

            [JsonProperty("Naming prefix to show that the item is enhanced. Leaving empty will not adjust the items name.")]
            public string item_name_prefix = "Enhanced";

            [JsonProperty("Perk settings")]
            public Dictionary<Perk, PerkSettings> perk_settings;

            [JsonProperty("List of skins for item types. If more than 1 skin is added, a random one will be selected when the item is created.")]
            public Dictionary<string, List<ulong>> item_skins = new Dictionary<string, List<ulong>>();

            [JsonProperty("Enhancement kit settings")]
            public EnhancementKitSettings enhancement_kit_settings = new EnhancementKitSettings();
        }

        public class LootSettings
        {
            [JsonProperty("List of items that cannot be enhanced")]
            public List<string> enhanceable_blacklist = new List<string>();

            [JsonProperty("List of items that can exclusively be enhanced [if items exist in this list, no other items will be enhanceable]")]
            public List<string> enhanceable_whitelist = new List<string>();
        }

        public class PerkSettings
        {
            public bool enabled;
            public float min_mod;
            public float max_mod;
            public int perkWeight;
            public List<string> whitelist;
            public List<string> blacklist;
            [JsonProperty("Perk modifier cap")]
            public float perk_cap;

            public PerkSettings(bool enabled, float min_mod, float max_mod, int perkWeight = 100, List<string> whitelist = null, List<string> blacklist = null, float player_mod_limit = 0)
            {
                this.enabled = enabled;
                this.min_mod = min_mod;
                this.max_mod = max_mod;
                this.perkWeight = perkWeight;
                this.whitelist = whitelist;
                this.blacklist = blacklist;
                this.perk_cap = player_mod_limit;
            }
        }

        public class EnhancementKitSettings
        {
            public string displayName = "enhancement kit:";
            public string shortname = "blood";
            public ulong skin = 2920198584;
        }
        #endregion

        #region Load/Save Config
        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();

                if (config == null)
                {
                    throw new JsonException();
                }
                Puts($"Configuration file {Name}.json loaded");
            }
            catch (Exception ex)
            {
                Puts($"Configuration file {Name}.json is invalid; using defaults");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Puts($"Configuration changes saved to {Name}.json");
            Config.WriteObject(config, true);
        }

        protected override void LoadDefaultConfig()
        {
            config = new Configuration();

            config.craft_settings.remove_perk_settings.weight_system.multiplier_1 = 1;
            config.craft_settings.remove_perk_settings.weight_system.multiplier_2 = 1;
            config.craft_settings.remove_perk_settings.weight_system.multiplier_3 = 2;

            config.craft_settings.randomize_perk_settings.randomize_perk_item.amount = 25;

            // sample values to showcase configuration
            config.craft_settings.add_perk_settings.craft_item.cost_per_kit.Add(Perk.BradleyDamage, 50);
            config.craft_settings.add_perk_settings.craft_item.cost_per_kit.Add(Perk.Deforest, 25);
            config.craft_settings.add_perk_settings.craft_item.cost_per_kit.Add(Perk.Fabricate, 25);
            config.craft_settings.add_perk_settings.craft_item.cost_per_kit.Add(Perk.HeliDamage, 50);
            config.craft_settings.add_perk_settings.craft_item.cost_per_kit.Add(Perk.ScientistBane, 50);
            config.craft_settings.add_perk_settings.craft_item.cost_per_kit.Add(Perk.ScientistWard, 25);
            config.craft_settings.add_perk_settings.craft_item.cost_per_kit.Add(Perk.UncannyDodge, 25);

            config.craft_settings.remove_perk_settings.craft_item.cost_per_kit.Add(Perk.BradleyDamage, 50);
            config.craft_settings.remove_perk_settings.craft_item.cost_per_kit.Add(Perk.Deforest, 25);
            config.craft_settings.remove_perk_settings.craft_item.cost_per_kit.Add(Perk.Fabricate, 25);
            config.craft_settings.remove_perk_settings.craft_item.cost_per_kit.Add(Perk.HeliDamage, 50);
            config.craft_settings.remove_perk_settings.craft_item.cost_per_kit.Add(Perk.ScientistBane, 50);
            config.craft_settings.remove_perk_settings.craft_item.cost_per_kit.Add(Perk.ScientistWard, 25);
            config.craft_settings.remove_perk_settings.craft_item.cost_per_kit.Add(Perk.UncannyDodge, 25);

            config.craft_settings.remove_perk_settings.craft_item.cost_per_kit.Add(Perk.BradleyDamage, 10);
            config.craft_settings.remove_perk_settings.craft_item.cost_per_kit.Add(Perk.Deforest, 5);
            config.craft_settings.remove_perk_settings.craft_item.cost_per_kit.Add(Perk.Fabricate, 5);
            config.craft_settings.remove_perk_settings.craft_item.cost_per_kit.Add(Perk.HeliDamage, 10);
            config.craft_settings.remove_perk_settings.craft_item.cost_per_kit.Add(Perk.ScientistBane, 5);
            config.craft_settings.remove_perk_settings.craft_item.cost_per_kit.Add(Perk.ScientistWard, 5);
            config.craft_settings.remove_perk_settings.craft_item.cost_per_kit.Add(Perk.UncannyDodge, 5);
        }

        private IEnumerator LoadEpicConfiguration()
        {
            string url = "file://" + Interface.Oxide.ConfigDirectory + Path.DirectorySeparatorChar + "EpicLoot.json";
            using (WWW www = new WWW(url))
            {
                yield return www;

                if (www.error == null)
                {
                    Puts("Configuration file EpicLoot.json found. Loading...");
                    epicConfig = JsonConvert.DeserializeObject<EpicLootConfiguration>(www.text);

                    GetEpicEnhanceableItems();
                }
                else
                {
                    Puts("Configuration file EpicLoot.json not found. Please fix your setup.");
                    Interface.Oxide.UnloadPlugin(Name);
                }
            }
        }

        // any chance we can really use that?
        public List<string> EpicEnhanceableItems = new List<string>();
        
        public void GetEpicEnhanceableItems()
        {
            foreach (var itemDef in ItemManager.GetItemDefinitions())
            {
                if (epicConfig.global_blacklist.Contains(itemDef.shortname)) continue;
                if (itemDef.category == ItemCategory.Attire && itemDef.isWearable && !itemDef.shortname.StartsWith("frankensteins.monster") && !EpicEnhanceableItems.Contains(itemDef.shortname))
                {
                    EpicEnhanceableItems.Add(itemDef.shortname);
                }

                if ((itemDef.category == ItemCategory.Weapon || itemDef.category == ItemCategory.Tool) && itemDef.isHoldable && (itemDef.occupySlots == ItemSlot.None || itemDef.occupySlots == 0) && !EpicEnhanceableItems.Contains(itemDef.shortname))
                {
                    EpicEnhanceableItems.Add(itemDef.shortname);
                }
            }
        }

        private IEnumerator LoadItemPerksConfiguration()
        {
            string url = "file://" + Interface.Oxide.ConfigDirectory + Path.DirectorySeparatorChar + "ItemPerks.json";
            using (WWW www = new WWW(url))
            {
                yield return www;

                if (www.error == null)
                {
                    Puts($"Configuration file ItemPerks.json found. Loading...");
                    perkConfig = JsonConvert.DeserializeObject<ItemPerksConfiguration>(www.text);

                    GetPerkEnhanceableItems();
                }
                else
                {
                    Puts($"Configuration file ItemPerks.json not found. Please fix your setup.");
                    Interface.Oxide.UnloadPlugin(Name);
                }
            }
        }

        public List<string> PerkEnhanceableItems = new List<string>();
        
        public void GetPerkEnhanceableItems()
        {
            foreach (var itemDef in ItemManager.GetItemDefinitions())
            {
                if (perkConfig.enhancementSettings.lootSettings.enhanceable_blacklist.Contains(itemDef.shortname)) continue;
                if (perkConfig.enhancementSettings.lootSettings.enhanceable_whitelist.Contains(itemDef.shortname))
                {
                    PerkEnhanceableItems.Add(itemDef.shortname);
                    continue;
                }
                if (itemDef.category == ItemCategory.Attire && itemDef.isWearable && !itemDef.shortname.StartsWith("frankensteins.monster") && !PerkEnhanceableItems.Contains(itemDef.shortname))
                {
                    PerkEnhanceableItems.Add(itemDef.shortname);
                    continue;
                }

                if ((itemDef.category == ItemCategory.Weapon || itemDef.category == ItemCategory.Tool) && itemDef.isHoldable && (itemDef.occupySlots == ItemSlot.None || itemDef.occupySlots == 0) && !PerkEnhanceableItems.Contains(itemDef.shortname))
                {
                    PerkEnhanceableItems.Add(itemDef.shortname);
                    continue;
                }
            }
        }

        #endregion

        #endregion

        #region Localization

        // NOTE: for perks and epic loot descriptions, we use the language files from those plugins -> no need to add all that stuff here
        protected override void LoadDefaultMessages()
        {
            Dictionary<string, string> langDict = new Dictionary<string, string>()
            {
                ["UICLOSE"] = "CLOSE",
                ["UI_OK"] = "OK",

                ["CMDADDPERK_HEADER"] = "Add a random Perk",
                ["CMDADDPERK_INFO"] = "Select up to {0} Kit(s) to increase your chance for a specific perk to be picked.\n<color=#FF0000>This process can fail!</color>",
                ["CMDREMOVEPERK_HEADER"] = "Remove a random Perk",
                ["CMDREMOVEPERK_INFO"] = "Select up to {0} Kit(s) to increase your chance for a specific perk to be picked.\n<color=#FF0000>This process can fail!</color>",
                ["CMDRANDOMIZEPERKVALUES_HEADER"] = "Reroll perk values",
                ["CMDRANDOMIZEPERKVALUES_INFO"] = "Select up to {0} Kit(s) to increase your chance for higher values.\n<color=#FF0000>Each kit can only apply to one mod value.</color>",

                ["UI_INVENTORY"] = "INVENTORY",
                ["UI_ITEM_DETAILS_DESCRIPTION"] = "Description",
                ["UI_ITEM_DETAILS_ACTIONS"] = "Actions",
                ["UI_ITEM_DETAILS_ADD_EPIC_BUFF"] = "Add Epic Buff",
                ["UI_ITEM_DETAILS_RECYCLE_EPIC"] = "Recycle Item",
                ["UI_ITEM_DETAILS_ADD_PERK_BUFF"] = "Add Perk",
                ["UI_ITEM_DETAILS_REMOVE_PERK"] = "Remove Perk",
                ["UI_ITEM_DETAILS_RANDOMIZE_PERKS"] = "Reroll Perk Values",

                ["UI_PLAYERBUFFS_TITLE"] = "PLAYER BUFFS",
                ["UI_PLAYERBUFFS_EPIC"] = "Epic Buffs",
                ["UI_PLAYERBUFFS_NO_EPIC"] = "No Epic Buffs available",
                ["UI_PLAYERBUFFS_PERKS"] = "Perk Buffs",
                ["UI_PLAYERBUFFS_NO_PERK"] = "No Perk Buffs available",

                ["UI_PERK_DETAILS_WEIGHT"] = "\nweight: <color=#ffb600>{0}</color>",

                ["UI_EPICBUFFDESCRIPTION_DESCRIPTION"] = "Description",
                ["UI_EPICBUFFDESCRIPTION_COST"] = "Cost",
                ["UI_EPICBUFFDESCRIPTION_SET_BONUS"] = "Set Bonus",
                ["UI_EPICBUFFDESCRIPTION_SET_PIECES"] = "- {0} pieces -",
                ["UI_EPICBUFFDESCRIPTION_SET_BONUS_DESCRIPTION"] = "<color=#077E93>{0}:</color> {1}",
                ["UI_EPICBUFFDESCRIPTION_SET_BONU_PERMISSION"] = "<color=#ffb600>Permission</color>",
                ["UI_EPICBUFFDESCRIPTION_TIERS"] = "Tiers",
                ["UI_EPICBUFFDESCRIPTION_CHANCES"] = "Chances",

                ["UI_EPICBUFFSELECTION_CONFIRM"] = "CONFIRM",
                ["UI_EPICBUFFSELECTION_CANCEL"] = "CANCEL",

                ["UI_PERKBUFFSELECTION_CONFIRM"] = "CONFIRM",
                ["UI_PERKBUFFSELECTION_CANCEL"] = "CANCEL",

                ["UI_ADDITIONAL_COST"] = "Additional Cost",
                ["UI_KITSELECTION"] = "Available Kits",
                ["UI_KITSELECTION_NONE"] = "No Kits found"
            };

            lang.RegisterMessages(langDict, this);
        }

        #endregion

        #region Helpers

        #region CommandLine

        // used to (de)serialize objects for button commands -> we can send lists etc :)
        public class CLI
        {
            public static string Serialize(System.Object obj)
            {
                return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(obj)));
            }

            public static T Deserialize<T>(string b64)
            {
                var decodedString = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                return JsonConvert.DeserializeObject<T>(decodedString);
            }
        }

        #endregion

        #endregion Helpers
    }
}