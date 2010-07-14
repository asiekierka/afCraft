// Copyright 2009, 2010 Matvei Stefarov <me@matvei.org>
using System;


namespace fCraft {
    enum BlockPlacementMode {
        Normal,
        Grass,
        Lava,
        Solid,
        Water,
        Hardened,
        RealWater,
        RealLava,
        ItemEnt,
        ItemEntRem
    }

    static class BlockCommands {

        // Register help commands
        internal static void Init(){
            Commands.AddCommand( "grass", Grass, false );
            Commands.AddCommand( "water", Water, false );
            Commands.AddCommand( "lava", Lava, false );
            Commands.AddCommand( "solid", Solid, false );
            Commands.AddCommand( "s", Solid, false );
            Commands.AddCommand( "paint", Paint, false );
            Commands.AddCommand( "harden", Hardened, false );
            Commands.AddCommand( "h", Hardened, false );
            Commands.AddCommand("rwater", RealWater, false);
            Commands.AddCommand("rlava", RealLava, false);
            Commands.AddCommand("ieadd", ItemEntAdd, false);
            Commands.AddCommand("ierem", ItemEntRem, false);
        }
        internal static void ItemEntRem(Player player, Command cmd)
        {
            if (player.ientmode == BlockPlacementMode.ItemEntRem)
            {
                player.ientmode = BlockPlacementMode.Normal;
                player.Message("ItemEntity remove mode: OFF");
            }
            else if (player.Can(Permissions.PlaceItemEnt))
            {
                player.ientmode = BlockPlacementMode.ItemEntRem;
                player.Message("ItemEntity remove mode: ON.");
            }
            else
            {
                player.NoAccessMessage(Permissions.PlaceItemEnt);
            }
        }
        internal static void ItemEntAdd(Player player, Command cmd)
        {
            string blockpar = cmd.Next();
            ItemEntType IPar1 = ItemEntType.Null;
            //byte IPar2 = 255;
            try { IPar1 = (ItemEntType)Convert.ToInt32(blockpar); }
            catch
            {
                try { IPar1 = Map.GetItemEntityByName(blockpar); }
                catch { player.Message("Incorrect parameter 1!"); return; }
            }
            /*
            blockpar = cmd.Next();
            if (blockpar != "")
            {
                try { IPar2 = Convert.ToByte(blockpar); }
                catch
                {
                    try { IPar2 = (byte)Map.GetBlockByName(blockpar); }
                    catch { }
                }
            }
            */
            if (player.ientmode == BlockPlacementMode.ItemEnt)
            {
                player.ientmode = BlockPlacementMode.Normal;
                player.Message("ItemEntity mode: OFF");
            }
            else if (player.Can(Permissions.PlaceItemEnt))
            {
                player.ientmode = BlockPlacementMode.ItemEnt;
                player.Message("ItemEntity mode: ON. Your next blocks will be ItemEntities.");
                player.ienttype = IPar1;
                /*
                if (IPar2 >= 0 && IPar2 <= 49)
                {
                    player.isIentBTDef = true;
                    player.ientbt = IPar2;
                }
                */
            }
            else
            {
                player.NoAccessMessage(Permissions.PlaceItemEnt);
            }
        }
        internal static void Solid( Player player, Command cmd ) {
            if( player.mode == BlockPlacementMode.Solid ){
                player.mode = BlockPlacementMode.Normal;
                player.Message( "Solid: OFF" );
            } else if( player.Can( Permissions.PlaceAdmincrete ) ) {
                player.mode = BlockPlacementMode.Solid;
                player.Message( "Solid: ON" );
            } else {
                player.NoAccessMessage( Permissions.PlaceAdmincrete );
            }
        }

        internal static void Hardened(Player player, Command cmd){
            if (player.hardenedMode == BlockPlacementMode.Hardened) {
                player.hardenedMode = BlockPlacementMode.Normal;
                player.Message("Hardened blocks: OFF");
            } else if (player.Can(Permissions.PlaceHardenedBlocks)) {
                player.hardenedMode = BlockPlacementMode.Hardened;
                player.Message("Hardened blocks: ON");
            } else {
                player.NoAccessMessage( Permissions.PlaceHardenedBlocks );
            }
        }


        internal static void Paint( Player player, Command cmd ) {
            player.replaceMode = !player.replaceMode;
            if( player.replaceMode ){
                player.Message( "Replacement mode: ON" );
            } else {
                player.Message( "Replacement mode: OFF" );
            }
        }


        internal static void Grass( Player player, Command cmd ) {
            if( player.mode == BlockPlacementMode.Grass ) {
                player.mode = BlockPlacementMode.Normal;
                player.Message( "Grass: OFF" );
            } else if( player.Can( Permissions.PlaceGrass ) ) {
                player.mode = BlockPlacementMode.Grass;
                player.Message( "Grass: ON. Dirt blocks are replaced with grass." );
            } else {
                player.NoAccessMessage( Permissions.PlaceGrass );
            }
        }

        #region Water/Lava
        internal static void Water( Player player, Command cmd ) {
            if( player.mode == BlockPlacementMode.Water ) {
                player.mode = BlockPlacementMode.Normal;
                player.Message( "Water: OFF" );
            } else if( player.Can( Permissions.PlaceWater ) ) {
                player.mode = BlockPlacementMode.Water;
                player.Message( "Water: ON. Blue blocks are replaced with water." );
            } else {
                player.NoAccessMessage( Permissions.PlaceWater );
            }
        }


        internal static void Lava( Player player, Command cmd ) {
            if( player.mode == BlockPlacementMode.Lava ) {
                player.mode = BlockPlacementMode.Normal;
                player.Message( "Lava: OFF." );
            } else if( player.Can( Permissions.PlaceLava ) ) {
                player.mode = BlockPlacementMode.Lava;
                player.Message( "Lava: ON. Red blocks are replaced with lava." );
            } else {
                player.NoAccessMessage( Permissions.PlaceLava );
            }
        }
        #endregion

        #region RealWater/RealLava

        internal static void RealWater(Player player, Command cmd)
        {
            if (player.mode == BlockPlacementMode.RealWater)
            {
                player.mode = BlockPlacementMode.Normal;
                player.Message("REAL Water: OFF");
            }
            else if (player.Can(Permissions.PlaceRealWater))
            {
                player.mode = BlockPlacementMode.RealWater;
                player.Message("REAL Water: ON. Blue blocks are replaced with water.");
            }
            else
            {
                player.NoAccessMessage(Permissions.PlaceRealWater);
            }
        }


        internal static void RealLava(Player player, Command cmd)
        {
            if (player.mode == BlockPlacementMode.RealLava)
            {
                player.mode = BlockPlacementMode.Normal;
                player.Message("REAL Lava: OFF.");
            }
            else if (player.Can(Permissions.PlaceRealLava))
            {
                player.mode = BlockPlacementMode.RealLava;
                player.Message("REAL Lava: ON. Red blocks are replaced with lava.");
            }
            else
            {
                player.NoAccessMessage(Permissions.PlaceRealLava);
            }
        }

        #endregion
    }
}
