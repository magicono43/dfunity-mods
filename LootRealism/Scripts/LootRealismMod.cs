// Project:         LootRealism mod for Daggerfall Unity (http://www.dfworkshop.net)
// Copyright:       Copyright (C) 2020
// License:         MIT License (http://www.opensource.org/licenses/mit-license.php)
// Authors:         Hazelnut & Ralzar

using System;
using System.Collections;
using UnityEngine;
using DaggerfallConnect;
using DaggerfallConnect.Save;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Utility;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using DaggerfallWorkshop.Game.Utility.ModSupport.ModSettings;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.Player;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;

namespace LootRealism
{
    public class LootRealismMod : MonoBehaviour
    {
        const int G = 85;   // Mob Array Gap from 42 .. 128 = 85
        static Mod mod;

        [Invoke(StateManager.StateTypes.Start, 0)]
        public static void Init(InitParams initParams)
        {
            mod = initParams.Mod;
            var go = new GameObject(mod.Title);
            go.AddComponent<LootRealismMod>();
        }

        void Awake()
        {
            ModSettings settings = mod.GetSettings();
            bool lootRebalance = settings.GetBool("Modules", "lootRebalance");
            bool bandaging = settings.GetBool("Modules", "bandaging");
            bool conditionBasedPrices = settings.GetBool("Modules", "conditionBasedPrices");
            bool enemyEquipment = settings.GetBool("Modules", "realisticEnemyEquipment");
            bool skillStartEquip = settings.GetBool("Modules", "skillBasedStartingEquipment");
            bool skillStartSpells = settings.GetBool("Modules", "skillBasedStartingSpells");

            InitMod(lootRebalance, bandaging, conditionBasedPrices, enemyEquipment, skillStartEquip, skillStartSpells);

            mod.IsReady = true;
        }

        private static void InitMod(bool lootRebalance, bool bandaging, bool conditionBasedPrices, bool enemyEquipment, bool skillStartEquip, bool skillStartSpells)
        {
            Debug.Log("Begin mod init: LootRealism");

            if (lootRebalance)
            {
                // Iterate over the new mob enemy data array and load into DFU enemies data.
                foreach (int mobDataId in MobLootKeys.Keys)
                {
                    // Log a message indicating the enemy mob being updated and update the loot key.
                    Debug.LogFormat("Updating enemy loot key for {0} to {1}.", EnemyBasics.Enemies[mobDataId].Name, MobLootKeys[mobDataId]);
                    EnemyBasics.Enemies[mobDataId].LootTableKey = (string) MobLootKeys[mobDataId];
                }
                // Replace the default loot matrix table with custom data.
                LootTables.DefaultLootTables = LootRealismTables;
            }

            if (bandaging)
            {
                if (DaggerfallUnity.Instance.ItemHelper.RegisterItemUseHander((int)UselessItems2.Bandage, UseBandage))
                    FormulaHelper.RegisterOverride(mod, "IsItemStackable", (Func<DaggerfallUnityItem, bool>)IsItemStackable);
                else
                    Debug.LogWarning("LootRealism: Unable to register bandage use handler.");
            }

            if (conditionBasedPrices)
            {
                FormulaHelper.RegisterOverride(mod, "ModifyFoundLootItems", (Func<DaggerfallUnityItem[], int>)RandomConditionFoundLootItems);
                FormulaHelper.RegisterOverride(mod, "CalculateCost", (Func<int, int, int, int>)CalculateConditionCost);
            }

            if (enemyEquipment)
            {
                EnemyEntity.AssignEnemyEquipment = AssignEnemyStartingEquipment;
            }

            StartGameBehaviour startGameBehaviour = GameManager.Instance.StartGameBehaviour;
            if (skillStartEquip)
            {
                startGameBehaviour.AssignStartingEquipment = AssignSkillEquipment;
            }
            if (skillStartSpells)
            {
                startGameBehaviour.AssignStartingSpells = AssignSkillSpellbook;
            }

            Debug.Log("Finished mod init: LootRealism");
        }

        public static bool IsItemStackable(DaggerfallUnityItem item)
        {
            return item.IsOfTemplate(ItemGroups.UselessItems2, (int)UselessItems2.Bandage);
        }

        static bool UseBandage(DaggerfallUnityItem item, ItemCollection collection)
        {
            if (collection != null)
            {
                PlayerEntity playerEntity = GameManager.Instance.PlayerEntity;
                int medical = playerEntity.Skills.GetLiveSkillValue(DFCareer.Skills.Medical);
                int heal = (int)Mathf.Min(medical / 3, playerEntity.MaxHealth * 0.4f);
                collection.RemoveOne(item);
                playerEntity.IncreaseHealth(heal);
                Debug.LogFormat("Applied a Bandage and healed {0} health.", heal);
            }
            return true;
        }

        public static int RandomConditionFoundLootItems(DaggerfallUnityItem[] lootItems)
        {
            int changes = 0;
            for (int i = 0; i < lootItems.Length; i++)
            {
                DaggerfallUnityItem item = lootItems[i];
                if ((item.ItemGroup == ItemGroups.Armor || item.ItemGroup == ItemGroups.Weapons || item.ItemGroup == ItemGroups.Books) && !item.IsArtifact)
                {
                    // Apply a random condition between 20% and 70%.
                    float conditionMod = UnityEngine.Random.Range(0.2f, 0.7f);
                    item.currentCondition = (int)(item.maxCondition * conditionMod);
                    changes++;
                }
            }
            return changes;
        }

        public static int CalculateConditionCost(int baseValue, int shopQuality, int conditionPercentage = -1)
        {
            float conditionMod = (conditionPercentage == -1) ? 1f : Mathf.Max((float)conditionPercentage / 100, 0.2f);

            int cost = (int)(baseValue * conditionMod);

            if (cost < 1)
                cost = 1;

            cost = FormulaHelper.ApplyRegionalPriceAdjustment(cost);
            cost = 2 * (cost * (shopQuality - 10) / 100 + cost);

            return cost;
        }

        static bool CoinFlip()
        {
            return UnityEngine.Random.Range(0, 2) == 0;
        }
        static Armor RandomShield()
        {
            return (Armor)UnityEngine.Random.Range((int)Armor.Buckler, (int)Armor.Round_Shield + 1);
        }
        static Weapons RandomLongblade()
        {
            return (Weapons)UnityEngine.Random.Range((int)Weapons.Broadsword, (int)Weapons.Longsword + 1);
        }
        static Weapons RandomBigWeapon()
        {
            Weapons weapon = (Weapons)UnityEngine.Random.Range((int)Weapons.Claymore, (int)Weapons.War_Axe + 1);
            if (weapon == Weapons.Dai_Katana && Dice100.SuccessRoll(95))
                weapon = Weapons.Claymore;  // Dai-katana's are really rare.
            return weapon;
        }
        static Weapons RandomBlunt()
        {
            return (Weapons)UnityEngine.Random.Range((int)Weapons.Mace, (int)Weapons.Warhammer + 1);
        }
        static Weapons RandomAxe()
        {
            return (Weapons)UnityEngine.Random.Range((int)Weapons.Battle_Axe, (int)Weapons.War_Axe + 1);
        }
        private static Weapons RandomAxeOrBlade()
        {
            return CoinFlip() ? RandomAxe() : RandomLongblade();
        }

        static Weapons RandomBow()
        {
            return (Weapons)UnityEngine.Random.Range((int)Weapons.Short_Bow, (int)Weapons.Long_Bow + 1);
        }
        static Weapons RandomShortblade()
        {
            if (Dice100.SuccessRoll(95))
                return CoinFlip() ? Weapons.Dagger : Weapons.Shortsword;
            else
                return (Weapons)UnityEngine.Random.Range((int)Weapons.Dagger, (int)Weapons.Wakazashi + 1);
        }
        static Weapons SecondaryWeapon()
        {
            switch (UnityEngine.Random.Range(0, 3))
            {
                case 0:
                    return Weapons.Dagger;
                case 1:
                    return Weapons.Shortsword;
                default:
                    return Weapons.Short_Bow;
            }
        }
        static Weapons GetCombatClassWeapon(MobileTypes enemyType)
        {
            switch (enemyType)
            {
                case MobileTypes.Barbarian:
                    return RandomBigWeapon();
                case MobileTypes.Knight:
                    return CoinFlip() ? RandomBlunt() : RandomLongblade();
                case MobileTypes.Knight_CityWatch:
                    return RandomAxeOrBlade();
                case MobileTypes.Monk:
                    return RandomBlunt();
                default:
                    return RandomLongblade();
            }
        }

        static void AddOrEquipWornItem(DaggerfallEntity entity, DaggerfallUnityItem item, bool equip = false)
        {
            entity.Items.AddItem(item);
            if (item.ItemGroup == ItemGroups.Armor || item.ItemGroup == ItemGroups.Weapons ||
                item.ItemGroup == ItemGroups.MensClothing || item.ItemGroup == ItemGroups.WomensClothing)
            {
                item.currentCondition = (int)(UnityEngine.Random.Range(0.3f, 0.7f) * item.maxCondition);
            }
            if (equip)
                entity.ItemEquipTable.EquipItem(item, true, false);
        }

        static void AssignEnemyStartingEquipment(PlayerEntity playerEntity, EnemyEntity enemyEntity, int variant)
        {
            // Use default code for non-class enemies.
            if (enemyEntity.EntityType != EntityTypes.EnemyClass)
            {
                DaggerfallUnity.Instance.ItemHelper.AssignEnemyStartingEquipment(playerEntity, enemyEntity, variant);
                return;
            }

            // Set item level, city watch never have items above iron or steel
            int itemLevel = (enemyEntity.MobileEnemy.ID == (int)MobileTypes.Knight_CityWatch) ? 1 : enemyEntity.Level;
            Genders playerGender = playerEntity.Gender;
            Races playerRace = playerEntity.Race;
            int chance = 50;

            // Held weapon(s) and shield/secondary:
            switch ((MobileTypes)enemyEntity.MobileEnemy.ID)
            {
                // Ranged specialists:
                case MobileTypes.Archer:
                case MobileTypes.Ranger:
                    AddOrEquipWornItem(enemyEntity, ItemBuilder.CreateWeapon(RandomBow(), ItemBuilder.RandomMaterial(itemLevel)), true);
                    AddOrEquipWornItem(enemyEntity, ItemBuilder.CreateWeapon((enemyEntity.MobileEnemy.ID == (int)MobileTypes.Ranger) ? RandomLongblade() : RandomShortblade(), ItemBuilder.RandomMaterial(itemLevel)));
                    DaggerfallUnityItem arrowPile = ItemBuilder.CreateWeapon(Weapons.Arrow, WeaponMaterialTypes.Iron);
                    arrowPile.stackCount = UnityEngine.Random.Range(4, 17);
                    enemyEntity.Items.AddItem(arrowPile);
                    chance = 55;
                    break;

                // Combat classes:
                case MobileTypes.Barbarian:
                case MobileTypes.Knight:
                case MobileTypes.Knight_CityWatch:
                case MobileTypes.Monk:
                case MobileTypes.Spellsword:
                case MobileTypes.Warrior:
                case MobileTypes.Rogue:
                    if (variant == 0)
                    {
                        AddOrEquipWornItem(enemyEntity, ItemBuilder.CreateWeapon(GetCombatClassWeapon((MobileTypes)enemyEntity.MobileEnemy.ID), ItemBuilder.RandomMaterial(itemLevel)), true);
                        // Left hand shield?
                        if (Dice100.SuccessRoll(chance))
                            AddOrEquipWornItem(enemyEntity, ItemBuilder.CreateArmor(playerGender, playerRace, RandomShield(), ItemBuilder.RandomArmorMaterial(itemLevel)), true);
                        // left-hand weapon?
                        else if (Dice100.SuccessRoll(chance))
                            AddOrEquipWornItem(enemyEntity, ItemBuilder.CreateWeapon(SecondaryWeapon(), ItemBuilder.RandomMaterial(itemLevel)));
                        chance = 60;
                    }
                    else
                    {
                        AddOrEquipWornItem(enemyEntity, ItemBuilder.CreateWeapon(RandomBigWeapon(), ItemBuilder.RandomMaterial(itemLevel)), true);
                        chance = 75;
                    }
                    if (enemyEntity.MobileEnemy.ID == (int)MobileTypes.Barbarian)
                        chance -= 45;   // Barbies tend to forgo armor
                    break;

                // Mage classes:
                case MobileTypes.Mage:
                case MobileTypes.Sorcerer:
                case MobileTypes.Healer:
                    AddOrEquipWornItem(enemyEntity, ItemBuilder.CreateWeapon(Weapons.Staff, ItemBuilder.RandomMaterial(itemLevel)), true);
                    if (Dice100.SuccessRoll(chance))
                        AddOrEquipWornItem(enemyEntity, ItemBuilder.CreateWeapon(RandomShortblade(), ItemBuilder.RandomMaterial(itemLevel)));
                    AddOrEquipWornItem(enemyEntity, (playerGender == Genders.Male) ? ItemBuilder.CreateMensClothing(MensClothing.Plain_robes, playerEntity.Race) : ItemBuilder.CreateWomensClothing(WomensClothing.Plain_robes, playerEntity.Race), true);
                    chance = 30;
                    break;

                // Sneaky classes:
                case MobileTypes.Acrobat:
                case MobileTypes.Assassin:
                case MobileTypes.Bard:
                case MobileTypes.Burglar:
                case MobileTypes.Nightblade:
                case MobileTypes.Thief:
                case MobileTypes.Battlemage:
                    AddOrEquipWornItem(enemyEntity, ItemBuilder.CreateWeapon((enemyEntity.MobileEnemy.ID == (int)MobileTypes.Battlemage) ? RandomAxeOrBlade() : RandomShortblade(), ItemBuilder.RandomMaterial(itemLevel)), true);
                    if (Dice100.SuccessRoll(chance))
                        AddOrEquipWornItem(enemyEntity, ItemBuilder.CreateWeapon(SecondaryWeapon(), ItemBuilder.RandomMaterial(itemLevel/2)), true);
                    chance = 45;
                    break;
            }

            // cuirass (everyone gets at least a 50% chance, knights and warriors allways have them)
            if (Dice100.SuccessRoll(Mathf.Max(chance, 50)) || enemyEntity.MobileEnemy.ID == (int)MobileTypes.Knight | enemyEntity.MobileEnemy.ID == (int)MobileTypes.Warrior)
                AddOrEquipWornItem(enemyEntity, ItemBuilder.CreateArmor(playerGender, playerRace, Armor.Cuirass, ItemBuilder.RandomArmorMaterial(itemLevel)), true);
            // greaves (Barbarians always get them)
            if (Dice100.SuccessRoll(chance) || enemyEntity.MobileEnemy.ID == (int)MobileTypes.Barbarian)
                AddOrEquipWornItem(enemyEntity, ItemBuilder.CreateArmor(playerGender, playerRace, Armor.Greaves, ItemBuilder.RandomArmorMaterial(itemLevel)), true);
            // helm (Barbarians always get them)
            if (Dice100.SuccessRoll(chance) || enemyEntity.MobileEnemy.ID == (int)MobileTypes.Barbarian)
                AddOrEquipWornItem(enemyEntity, ItemBuilder.CreateArmor(playerGender, playerRace, Armor.Helm, ItemBuilder.RandomArmorMaterial(itemLevel)), true);
            // boots
            if (Dice100.SuccessRoll(chance))
                AddOrEquipWornItem(enemyEntity, ItemBuilder.CreateArmor(playerGender, playerRace, Armor.Boots, ItemBuilder.RandomArmorMaterial(itemLevel)), true);

            if (chance > 50)
            {
                chance -= 15;
                // right pauldron
                if (Dice100.SuccessRoll(chance))
                    AddOrEquipWornItem(enemyEntity, ItemBuilder.CreateArmor(playerGender, playerRace, Armor.Right_Pauldron, ItemBuilder.RandomArmorMaterial(itemLevel)), true);
                // left pauldron
                if (Dice100.SuccessRoll(chance))
                    AddOrEquipWornItem(enemyEntity, ItemBuilder.CreateArmor(playerGender, playerRace, Armor.Left_Pauldron, ItemBuilder.RandomArmorMaterial(itemLevel)), true);
                // gauntlets
                if (Dice100.SuccessRoll(chance))
                    AddOrEquipWornItem(enemyEntity, ItemBuilder.CreateArmor(playerGender, playerRace, Armor.Gauntlets, ItemBuilder.RandomArmorMaterial(itemLevel)), true);
            }

            // Chance for poisoned weapon
            if (playerEntity.Level > 1)
            {
                DaggerfallUnityItem mainWeapon = enemyEntity.ItemEquipTable.GetItem(EquipSlots.RightHand);
                if (mainWeapon != null)
                {
                    int chanceToPoison = 5;
                    if (enemyEntity.MobileEnemy.ID == (int)MobileTypes.Assassin)
                        chanceToPoison = 60;

                    if (Dice100.SuccessRoll(chanceToPoison))
                    {
                        // Apply poison
                        mainWeapon.poisonType = (Poisons)UnityEngine.Random.Range(128, 135 + 1);
                    }
                }
            }
        }

        static void AssignSkillEquipment(PlayerEntity playerEntity, CharacterDocument characterDocument)
        {
            Debug.Log("Starting Equipment: Assigning Based on Skills");

            // Set condition of ebony dagger if player has one from char creation questions
            IList daggers = playerEntity.Items.SearchItems(ItemGroups.Weapons, (int)Weapons.Dagger);
            foreach (DaggerfallUnityItem dagger in daggers)
                if (dagger.NativeMaterialValue > (int)WeaponMaterialTypes.Steel)
                    dagger.currentCondition = (int)(dagger.maxCondition * 0.15);

            // Skill based items
            AssignSkillItems(playerEntity, playerEntity.Career.PrimarySkill1);
            AssignSkillItems(playerEntity, playerEntity.Career.PrimarySkill2);
            AssignSkillItems(playerEntity, playerEntity.Career.PrimarySkill3);

            AssignSkillItems(playerEntity, playerEntity.Career.MajorSkill1);
            AssignSkillItems(playerEntity, playerEntity.Career.MajorSkill2);
            AssignSkillItems(playerEntity, playerEntity.Career.MajorSkill3);

            // Starting clothes are gender-specific, randomise shirt dye and pants variant
            DaggerfallUnityItem shortShirt = null;
            DaggerfallUnityItem casualPants = null;
            if (playerEntity.Gender == Genders.Female)
            {
                shortShirt = ItemBuilder.CreateWomensClothing(WomensClothing.Short_shirt_closed, playerEntity.Race, 0, ItemBuilder.RandomClothingDye());
                casualPants = ItemBuilder.CreateWomensClothing(WomensClothing.Casual_pants, playerEntity.Race);
            }
            else
            {
                shortShirt = ItemBuilder.CreateMensClothing(MensClothing.Short_shirt, playerEntity.Race, 0, ItemBuilder.RandomClothingDye());
                casualPants = ItemBuilder.CreateMensClothing(MensClothing.Casual_pants, playerEntity.Race);
            }
            ItemBuilder.RandomizeClothingVariant(casualPants);
            AddOrEquipWornItem(playerEntity, shortShirt, true);
            AddOrEquipWornItem(playerEntity, casualPants, true);

            // Add spellbook, all players start with one - also a little gold and a crappy iron dagger for those with no weapon skills.
            playerEntity.Items.AddItem(ItemBuilder.CreateItem(ItemGroups.MiscItems, (int)MiscItems.Spellbook));
            playerEntity.Items.AddItem(ItemBuilder.CreateGoldPieces(UnityEngine.Random.Range(5, playerEntity.Career.Luck)));
            playerEntity.Items.AddItem(ItemBuilder.CreateWeapon(Weapons.Dagger, WeaponMaterialTypes.Iron));

            // Add some torches and candles if player torch is from items setting enabled
            if (DaggerfallUnity.Settings.PlayerTorchFromItems)
            {
                for (int i = 0; i < 2; i++)
                    playerEntity.Items.AddItem(ItemBuilder.CreateItem(ItemGroups.UselessItems2, (int)UselessItems2.Torch));
                for (int i = 0; i < 4; i++)
                    playerEntity.Items.AddItem(ItemBuilder.CreateItem(ItemGroups.UselessItems2, (int)UselessItems2.Candle));
            }

            Debug.Log("Starting Equipment: Assigning Finished");
        }

        static void AssignSkillItems(PlayerEntity playerEntity, DFCareer.Skills skill)
        {
            ItemCollection items = playerEntity.Items;
            Genders gender = playerEntity.Gender;
            Races race = playerEntity.Race;

            bool upgrade = Dice100.SuccessRoll(playerEntity.Career.Luck / (playerEntity.Career.Luck < 56 ? 2 : 1));
            WeaponMaterialTypes weaponMaterial = WeaponMaterialTypes.Iron;
            if ((upgrade && !playerEntity.Career.IsMaterialForbidden(DFCareer.MaterialFlags.Steel)) || playerEntity.Career.IsMaterialForbidden(DFCareer.MaterialFlags.Iron))
            {
                weaponMaterial = WeaponMaterialTypes.Steel;
            }
            ArmorMaterialTypes armorMaterial = ArmorMaterialTypes.Leather;
            if ((upgrade && !playerEntity.Career.IsArmorForbidden(DFCareer.ArmorFlags.Chain)) || playerEntity.Career.IsArmorForbidden(DFCareer.ArmorFlags.Leather))
            {
                armorMaterial = ArmorMaterialTypes.Chain;
            }

            switch (skill)
            {
                case DFCareer.Skills.Archery:
                    AddOrEquipWornItem(playerEntity, ItemBuilder.CreateWeapon(Weapons.Short_Bow, weaponMaterial));
                    DaggerfallUnityItem arrowPile = ItemBuilder.CreateWeapon(Weapons.Arrow, WeaponMaterialTypes.Iron);
                    arrowPile.stackCount = 30;
                    items.AddItem(arrowPile);
                    return;
                case DFCareer.Skills.Axe:
                    AddOrEquipWornItem(playerEntity, ItemBuilder.CreateWeapon(Dice100.SuccessRoll(50) ? Weapons.Battle_Axe : Weapons.War_Axe, weaponMaterial)); return;
                case DFCareer.Skills.Backstabbing:
                    AddOrEquipWornItem(playerEntity, ItemBuilder.CreateArmor(gender, race, Armor.Right_Pauldron, armorMaterial)); return;
                case DFCareer.Skills.BluntWeapon:
                    AddOrEquipWornItem(playerEntity, ItemBuilder.CreateWeapon(Dice100.SuccessRoll(50) ? Weapons.Mace : Weapons.Flail, weaponMaterial)); return;
                case DFCareer.Skills.Climbing:
                    AddOrEquipWornItem(playerEntity, ItemBuilder.CreateArmor(gender, race, Armor.Helm, armorMaterial, -1)); return;
                case DFCareer.Skills.CriticalStrike:
                    AddOrEquipWornItem(playerEntity, ItemBuilder.CreateArmor(gender, race, Armor.Greaves, armorMaterial)); return;
                case DFCareer.Skills.Dodging:
                    AddOrEquipWornItem(playerEntity, (gender == Genders.Male) ? ItemBuilder.CreateMensClothing(MensClothing.Casual_cloak, race) : ItemBuilder.CreateWomensClothing(WomensClothing.Casual_cloak, race)); return;
                case DFCareer.Skills.Etiquette:
                    AddOrEquipWornItem(playerEntity, (gender == Genders.Male) ? ItemBuilder.CreateMensClothing(MensClothing.Formal_tunic, race) : ItemBuilder.CreateWomensClothing(WomensClothing.Evening_gown, race)); return;
                case DFCareer.Skills.HandToHand:
                    AddOrEquipWornItem(playerEntity, ItemBuilder.CreateArmor(gender, race, Armor.Gauntlets, armorMaterial)); return;
                case DFCareer.Skills.Jumping:
                    AddOrEquipWornItem(playerEntity, ItemBuilder.CreateArmor(gender, race, Armor.Boots, armorMaterial)); return;
                case DFCareer.Skills.Lockpicking:
                    items.AddItem(ItemBuilder.CreateRandomPotion()); return;
                case DFCareer.Skills.LongBlade:
                    AddOrEquipWornItem(playerEntity, ItemBuilder.CreateWeapon(Dice100.SuccessRoll(50) ? Weapons.Saber : Weapons.Broadsword, weaponMaterial)); return;
                case DFCareer.Skills.Medical:
                    DaggerfallUnityItem bandages = ItemBuilder.CreateItem(ItemGroups.UselessItems2, (int)UselessItems2.Bandage);
                    bandages.stackCount = 4;
                    items.AddItem(bandages);
                    return;
                case DFCareer.Skills.Mercantile:
                    items.AddItem(ItemBuilder.CreateGoldPieces(UnityEngine.Random.Range(playerEntity.Career.Luck, playerEntity.Career.Luck * 4))); return;
                case DFCareer.Skills.Pickpocket:
                    items.AddItem(ItemBuilder.CreateRandomGem()); return;
                case DFCareer.Skills.Running:
                    AddOrEquipWornItem(playerEntity, (gender == Genders.Male) ? ItemBuilder.CreateMensClothing(MensClothing.Shoes, race) : ItemBuilder.CreateWomensClothing(WomensClothing.Shoes, race)); return;
                case DFCareer.Skills.ShortBlade:
                    AddOrEquipWornItem(playerEntity, ItemBuilder.CreateWeapon(Dice100.SuccessRoll(50) ? Weapons.Shortsword : Weapons.Tanto, weaponMaterial)); return;
                case DFCareer.Skills.Stealth:
                    AddOrEquipWornItem(playerEntity, (gender == Genders.Male) ? ItemBuilder.CreateMensClothing(MensClothing.Khajiit_suit, race) : ItemBuilder.CreateWomensClothing(WomensClothing.Khajiit_suit, race)); return;
                case DFCareer.Skills.Streetwise:
                    AddOrEquipWornItem(playerEntity, ItemBuilder.CreateArmor(gender, race, Armor.Cuirass, armorMaterial)); return;
                case DFCareer.Skills.Swimming:
                    items.AddItem((gender == Genders.Male) ? ItemBuilder.CreateMensClothing(MensClothing.Loincloth, race) : ItemBuilder.CreateWomensClothing(WomensClothing.Loincloth, race)); return;

                case DFCareer.Skills.Daedric:
                case DFCareer.Skills.Dragonish:
                case DFCareer.Skills.Giantish:
                case DFCareer.Skills.Harpy:
                case DFCareer.Skills.Impish:
                case DFCareer.Skills.Orcish:
                    items.AddItem(ItemBuilder.CreateRandomBook());
                    for (int i = 0; i < 4; i++)
                        items.AddItem(ItemBuilder.CreateRandomIngredient(ItemGroups.CreatureIngredients1));
                    return;
                case DFCareer.Skills.Centaurian:
                case DFCareer.Skills.Nymph:
                case DFCareer.Skills.Spriggan:
                    items.AddItem(ItemBuilder.CreateRandomBook());
                    for (int i = 0; i < 4; i++)
                        items.AddItem(ItemBuilder.CreateRandomIngredient(ItemGroups.PlantIngredients1));
                    return;
            }
        }

        static void AssignSkillSpellbook(PlayerEntity playerEntity, CharacterDocument characterDocument)
        {
            Debug.Log("Starting Spells: Assigning Based on Skills");

            // Skill based items
            AssignSkillSpells(playerEntity, playerEntity.Career.PrimarySkill1, true);
            AssignSkillSpells(playerEntity, playerEntity.Career.PrimarySkill2, true);
            AssignSkillSpells(playerEntity, playerEntity.Career.PrimarySkill3, true);

            AssignSkillSpells(playerEntity, playerEntity.Career.MajorSkill1);
            AssignSkillSpells(playerEntity, playerEntity.Career.MajorSkill2);
            AssignSkillSpells(playerEntity, playerEntity.Career.MajorSkill3);

            Debug.Log("Starting Spells: Assigning Finished");
        }

        static void AssignSkillSpells(PlayerEntity playerEntity, DFCareer.Skills skill, bool primary = false)
        {
            ItemCollection items = playerEntity.Items;
            Genders gender = playerEntity.Gender;
            Races race = playerEntity.Race;

            switch (skill)
            {
                // Classic spell indexes are on https://en.uesp.net/wiki/Daggerfall:SPELLS.STD_indices under Spell Catalogue
                case DFCareer.Skills.Alteration:
                    playerEntity.AddSpell(gentleFallSpell);         // Gentle Fall
                    if (primary)
                        playerEntity.AddSpell(GetClassicSpell(38)); // Jumping
                    return;
                case DFCareer.Skills.Destruction:
                    playerEntity.AddSpell(arcaneArrowSpell);        // Arcane Arrow
                    if (primary)
                        playerEntity.AddSpell(minorShockSpell);     // Minor shock
                    return;
                case DFCareer.Skills.Illusion:
                    playerEntity.AddSpell(candleSpell);             // Candle
                    if (primary)
                        playerEntity.AddSpell(GetClassicSpell(44)); // Chameleon
                    return;
                case DFCareer.Skills.Mysticism:
                    playerEntity.AddSpell(knockSpell);              // Knock
                    playerEntity.AddSpell(knickKnackSpell);         // Knick-Knack
                    if (primary) {
                        playerEntity.AddSpell(GetClassicSpell(1));  // Fenrik's Door Jam
                        playerEntity.AddSpell(GetClassicSpell(94)); // Recall!
                    }
                    return;
                case DFCareer.Skills.Restoration:
                    playerEntity.AddSpell(salveBruiseSpell);         // Salve Bruise
                    if (primary) {
                        playerEntity.AddSpell(smellingSaltsSpell);  // Smelling Salts
                        playerEntity.AddSpell(GetClassicSpell(97)); // Balyna's Balm
                    }
                    return;
                case DFCareer.Skills.Thaumaturgy:
                    playerEntity.AddSpell(GetClassicSpell(2));      // Buoyancy
                    if (primary)
                        playerEntity.AddSpell(riseSpell);           // Rise
                    return;
            }
        }

        static EffectBundleSettings GetClassicSpell(int spellId)
        {
            SpellRecord.SpellRecordData spellData;
            GameManager.Instance.EntityEffectBroker.GetClassicSpellRecord(spellId, out spellData);
            EffectBundleSettings bundle;
            GameManager.Instance.EntityEffectBroker.ClassicSpellRecordDataToEffectBundleSettings(spellData, BundleTypes.Spell, out bundle);
            return bundle;
        }

        // New spell definitions:
        static EffectBundleSettings minorShockSpell = new EffectBundleSettings()
        {
            Name = "Minor Shock",
            Version = EntityEffectBroker.CurrentSpellVersion,
            BundleType = BundleTypes.Spell,
            TargetType = TargetTypes.ByTouch,
            ElementType = ElementTypes.Shock,
            Icon = new SpellIcon() { index = 37 },
            Effects = new EffectEntry[]
            {   // Uses damage health effect class which needs magnitude defined in settings.
                new EffectEntry(DamageHealth.EffectKey, new EffectSettings() {
                        // 2-10 + 1-2 per 1 lev
                        MagnitudeBaseMin = 2, MagnitudeBaseMax = 10,
                        MagnitudePlusMin = 1, MagnitudePlusMax = 2, MagnitudePerLevel = 1
                    })
            },
        };
        static EffectBundleSettings arcaneArrowSpell = new EffectBundleSettings()
        {
            Name = "Arcane Arrow",
            Version = EntityEffectBroker.CurrentSpellVersion,
            BundleType = BundleTypes.Spell,
            TargetType = TargetTypes.SingleTargetAtRange,
            ElementType = ElementTypes.Magic,
            Icon = new SpellIcon() { index = 57 },
            Effects = new EffectEntry[]
            {
                new EffectEntry(DamageHealth.EffectKey, new EffectSettings() {
                    MagnitudeBaseMin = 5, MagnitudeBaseMax = 6,
                    MagnitudePlusMin = 1, MagnitudePlusMax = 1, MagnitudePerLevel = 2
                })
            },
        };
        static EffectBundleSettings gentleFallSpell = new EffectBundleSettings()
        {
            Name = "Gentle Fall",
            Version = EntityEffectBroker.CurrentSpellVersion,
            BundleType = BundleTypes.Spell,
            TargetType = TargetTypes.CasterOnly,
            ElementType = ElementTypes.Magic,
            Icon = new SpellIcon() { index = 31 },
            Effects = new EffectEntry[]
            {
                new EffectEntry(Slowfall.EffectKey, new EffectSettings() {
                        DurationBase = 2, DurationPlus = 1, DurationPerLevel = 2
                    })
            },
        };
        static EffectBundleSettings candleSpell = new EffectBundleSettings()
        {
            Name = "Candle",
            Version = EntityEffectBroker.CurrentSpellVersion,
            BundleType = BundleTypes.Spell,
            TargetType = TargetTypes.CasterOnly,
            ElementType = ElementTypes.Magic,
            Icon = new SpellIcon() { index = 22 },
            Effects = new EffectEntry[]
            {
                new EffectEntry(LightNormal.EffectKey, new EffectSettings() {
                    DurationBase = 4, DurationPlus = 4, DurationPerLevel = 1
                })
            },
        };
        static EffectBundleSettings knickKnackSpell = new EffectBundleSettings()
        {
            Name = "Knick-Knack",
            Version = EntityEffectBroker.CurrentSpellVersion,
            BundleType = BundleTypes.Spell,
            TargetType = TargetTypes.CasterOnly,
            ElementType = ElementTypes.Magic,
            Icon = new SpellIcon() { index = 26 },
            Effects = new EffectEntry[]
            {
                new EffectEntry(CreateItem.EffectKey, new EffectSettings() {
                    DurationBase = 4, DurationPlus = 1, DurationPerLevel = 2
                })
            },
        };
        static EffectBundleSettings salveBruiseSpell = new EffectBundleSettings()
        {
            Name = "Salve Bruise",
            Version = EntityEffectBroker.CurrentSpellVersion,
            BundleType = BundleTypes.Spell,
            TargetType = TargetTypes.CasterOnly,
            ElementType = ElementTypes.Magic,
            Icon = new SpellIcon() { index = 13 },
            Effects = new EffectEntry[]
            {
                new EffectEntry(HealHealth.EffectKey, new EffectSettings() {
                    MagnitudeBaseMin = 3, MagnitudeBaseMax = 6, MagnitudePerLevel = 1
                })
             },
        };
        static EffectBundleSettings smellingSaltsSpell = new EffectBundleSettings()
        {
            Name = "Smelling Salts",
            Version = EntityEffectBroker.CurrentSpellVersion,
            BundleType = BundleTypes.Spell,
            TargetType = TargetTypes.CasterOnly,
            ElementType = ElementTypes.Magic,
            Icon = new SpellIcon() { index = 10 },
            Effects = new EffectEntry[]
            {
                new EffectEntry(HealFatigue.EffectKey, new EffectSettings() {
                    MagnitudeBaseMin = 3, MagnitudeBaseMax = 6, MagnitudePerLevel = 1
                })
             },
        };
        static EffectBundleSettings riseSpell = new EffectBundleSettings()
        {
            Name = "Rise",
            Version = EntityEffectBroker.CurrentSpellVersion,
            BundleType = BundleTypes.Spell,
            TargetType = TargetTypes.CasterOnly,
            ElementType = ElementTypes.Magic,
            Icon = new SpellIcon() { index = 13 },
            Effects = new EffectEntry[]
            {
                new EffectEntry(Levitate.EffectKey, new EffectSettings() {
                    DurationBase = 1, DurationPlus = 1, DurationPerLevel = 2
                })
            },
        };
        static EffectBundleSettings knockSpell = new EffectBundleSettings()
        {
            Name = "Knock",
            Version = EntityEffectBroker.CurrentSpellVersion,
            BundleType = BundleTypes.Spell,
            TargetType = TargetTypes.CasterOnly,
            ElementType = ElementTypes.Magic,
            Icon = new SpellIcon() { index = 3 },
            Effects = new EffectEntry[]
            {
                new EffectEntry(Open.EffectKey, new EffectSettings() {
                    DurationBase = 1, DurationPlus = 1, ChanceBase = 8, ChancePerLevel = 2
                })
            },
        };

        static IDictionary MobLootKeys = new Hashtable()
        {
            {9, "F"},       // Giant
            {10, "B"},      // Nymph
            {19, "J"},      // Mummy
            {21, "LR2"},    // Orc Shaman
            {32, "J"},      // Lich
            {33, "S"},      // Ancient Lich
            {26, "S"},      // Fire Daedra
            {27, "S"},      // Daedroth
            {29, "S"},      // Daedra Seducer
            {128-G, "LR3"}, // Mage
            {130-G, "LR3"}, // Battlemage
            {131-G, "LR3"}, // Sorcerer
            {132-G, "LR2"}, // Healer
            {133-G, "LR3"}, // Nightblade
            {140-G, "O"},   // Monk
            {143-G, "LR1"}, // Barbarian
            {144-G, "LR1"}, // Warrior
        };

        static readonly LootChanceMatrix[] LootRealismTables = {
            new LootChanceMatrix() {key = "-",   MinGold = 0,   MaxGold = 0,    P1 = 0, P2 = 0, C1 = 0, C2 = 0, C3 = 0, M1 = 0, AM = 0,  WP = 0,  MI = 0, CL = 0, BK = 0, M2 = 0, RL = 0 }, //None
            new LootChanceMatrix() {key = "A",   MinGold = 0,   MaxGold = 5,    P1 = 0, P2 = 0, C1 = 1, C2 = 1, C3 = 1, M1 = 0, AM = 1,  WP = 5,  MI = 1, CL = 5, BK = 0, M2 = 1, RL = 0 }, //Orcs
            new LootChanceMatrix() {key = "B",   MinGold = 0,   MaxGold = 0,    P1 = 5, P2 = 5, C1 = 0, C2 = 0, C3 = 0, M1 = 0, AM = 0,  WP = 0,  MI = 0, CL = 0, BK = 0, M2 = 0, RL = 0 }, //Nature
            new LootChanceMatrix() {key = "C",   MinGold = 0,   MaxGold = 10,   P1 = 5, P2 = 5, C1 = 3, C2 = 3, C3 = 3, M1 = 3, AM = 10, WP = 20, MI = 1, CL = 50,BK = 2, M2 = 2, RL = 1 }, //Rangers
            new LootChanceMatrix() {key = "D",   MinGold = 0,   MaxGold = 0,    P1 = 2, P2 = 2, C1 = 2, C2 = 2, C3 = 2, M1 = 2, AM = 0,  WP = 0,  MI = 0, CL = 0, BK = 0, M2 = 0, RL = 0 }, //Harpy
            new LootChanceMatrix() {key = "E",   MinGold = 0,   MaxGold = 10,   P1 = 2, P2 = 2, C1 = 5, C2 = 5, C3 = 5, M1 = 2, AM = 0,  WP = 5,  MI = 0, CL = 0, BK = 0, M2 = 1, RL = 2 }, //Giant
            new LootChanceMatrix() {key = "F",   MinGold = 2,   MaxGold = 15,   P1 = 2, P2 = 2, C1 = 5, C2 = 5, C3 = 5, M1 = 2, AM = 80, WP = 70, MI = 2, CL = 0, BK = 0, M2 = 3, RL = 10 },//Giant Loot
            new LootChanceMatrix() {key = "G",   MinGold = 0,   MaxGold = 8,    P1 = 0, P2 = 0, C1 = 0, C2 = 0, C3 = 0, M1 = 0, AM = 10, WP = 1,  MI = 1, CL = 10,BK = 0, M2 = 3, RL = 5 }, //Undead naked
            new LootChanceMatrix() {key = "H",   MinGold = 0,   MaxGold = 5,    P1 = 0, P2 = 0, C1 = 0, C2 = 0, C3 = 0, M1 = 0, AM = 5,  WP = 30, MI = 1, CL = 2, BK = 1, M2 = 0, RL = 5 }, //Undead armed
            new LootChanceMatrix() {key = "I",   MinGold = 0,   MaxGold = 0,    P1 = 0, P2 = 0, C1 = 0, C2 = 0, C3 = 0, M1 = 0, AM = 0,  WP = 0,  MI = 1, CL = 0, BK = 0, M2 = 0, RL = 5 }, //Undead spirits
            new LootChanceMatrix() {key = "J",   MinGold = 10,  MaxGold = 40,   P1 = 3, P2 = 3, C1 = 3, C2 = 3, C3 = 3, M1 = 5, AM = 1,  WP = 1,  MI = 5, CL = 5, BK = 5, M2 = 2, RL = 10 },//Undead bosses
            new LootChanceMatrix() {key = "K",   MinGold = 10,  MaxGold = 20,   P1 = 3, P2 = 3, C1 = 3, C2 = 3, C3 = 3, M1 = 3, AM = 70, WP = 50, MI = 3, CL = 0, BK = 2, M2 = 2, RL = 80 },//Undead Loot
            new LootChanceMatrix() {key = "L",   MinGold = 0,   MaxGold = 10,   P1 = 0, P2 = 0, C1 = 3, C2 = 3, C3 = 3, M1 = 3, AM = 70, WP = 50, MI = 2, CL = 20,BK = 0, M2 = 3, RL = 3 }, //Nest Loot
            new LootChanceMatrix() {key = "M",   MinGold = 0,   MaxGold = 7,    P1 = 1, P2 = 1, C1 = 1, C2 = 1, C3 = 1, M1 = 2, AM = 80, WP = 40, MI = 2, CL = 15,BK = 2, M2 = 3, RL = 1 }, //Cave Loot
            new LootChanceMatrix() {key = "N",   MinGold = 1,   MaxGold = 40,   P1 = 5, P2 = 5, C1 = 5, C2 = 5, C3 = 5, M1 = 5, AM = 90, WP = 60, MI = 2, CL = 20,BK = 5, M2 = 2, RL = 5 }, //Castle Loot
            new LootChanceMatrix() {key = "O",   MinGold = 0,   MaxGold = 30,   P1 = 1, P2 = 1, C1 = 1, C2 = 1, C3 = 1, M1 = 1, AM = 5,  WP = 10, MI = 1, CL = 60,BK = 0, M2 = 0, RL = 0 }, //Rogues
            new LootChanceMatrix() {key = "P",   MinGold = 2,   MaxGold = 10,   P1 = 5, P2 = 2, C1 = 2, C2 = 2, C3 = 2, M1 = 2, AM = 10, WP = 10, MI = 2, CL = 50,BK = 9, M2 = 2, RL = 0 }, //Spellsword
            new LootChanceMatrix() {key = "Q",   MinGold = 10,  MaxGold = 40,   P1 = 2, P2 = 2, C1 = 4, C2 = 4, C3 = 4, M1 = 2, AM = 0,  WP = 0,  MI = 2, CL = 70,BK = 5, M2 = 3, RL = 5 }, //Vampires
            new LootChanceMatrix() {key = "R",   MinGold = 2,   MaxGold = 10,   P1 = 0, P2 = 0, C1 = 3, C2 = 3, C3 = 3, M1 = 5, AM = 0,  WP = 0,  MI = 1, CL = 0, BK = 0, M2 = 2, RL = 0 }, //Water monsters
            new LootChanceMatrix() {key = "S",   MinGold = 30,  MaxGold = 70,   P1 = 5, P2 = 5, C1 = 5, C2 = 5, C3 = 5, M1 = 15,AM = 0,  WP = 0,  MI = 8, CL = 5, BK = 5, M2 = 2, RL = 10 },//Dragon Loot, Daedras
            new LootChanceMatrix() {key = "T",   MinGold = 10,  MaxGold = 50,   P1 = 0, P2 = 0, C1 = 0, C2 = 0, C3 = 0, M1 = 0, AM = 60, WP = 40, MI = 0, CL = 50,BK = 10,M2 = 0, RL = 10}, //Knight Orc Warlord
            new LootChanceMatrix() {key = "U",   MinGold = 0,   MaxGold = 40,   P1 = 5, P2 = 5, C1 = 5, C2 = 5, C3 = 5, M1 = 10,AM = 20, WP = 20, MI = 4, CL = 20,BK = 90,M2 = 5, RL = 70 },//Laboratory Loot
            new LootChanceMatrix() {key = "LR1", MinGold = 0,   MaxGold = 20,   P1 = 0, P2 = 0, C1 = 0, C2 = 0, C3 = 0, M1 = 0, AM = 40, WP = 50, MI = 1, CL = 50,BK = 0, M2 = 0, RL = 5 }, //Warrior, Barbarian
            new LootChanceMatrix() {key = "LR2", MinGold = 0,   MaxGold = 5,    P1 = 3, P2 = 3, C1 = 1, C2 = 1, C3 = 3, M1 = 3, AM = 0,  WP = 20, MI = 1, CL = 60,BK = 10,M2 = 3, RL = 0 }, //Healer, Orc Shaman
            new LootChanceMatrix() {key = "LR3", MinGold = 0,   MaxGold = 30,   P1 = 3, P2 = 3, C1 = 1, C2 = 1, C3 = 1, M1 = 2, AM = 0,  WP = 20, MI = 1, CL = 95,BK = 45,M2 = 2, RL = 10 },//Spellcasters
        };
    }
}
