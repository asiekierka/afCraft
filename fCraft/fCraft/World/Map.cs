﻿// Copyright 2009, 2010 Matvei Stefarov <me@matvei.org>

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using mcc;


namespace fCraft {
    public sealed class Map {

        internal World world;
        internal byte[] blocks;
        public int widthX, widthY, height;
        public Position spawn;
        public Dictionary<string, string> meta = new Dictionary<string, string>();
        Queue<BlockUpdate> updates = new Queue<BlockUpdate>();
        object queueLock = new object(), metaLock = new object(), zoneLock = new object();
        object pqLock = new object();
        public int changesSinceSave, changesSinceBackup;
        Queue<BlockUpdate> postupdates = new Queue<BlockUpdate>();
        public List<ItemEntity> ietlist = new List<ItemEntity>();
        object ietlock = new object();
        public List<Position> tntpl = new List<Position>();
        object tntplock = new object();

        internal Map() { }

        public Map( World _world ) {
            world = _world;
        }

        // creates an empty new world of specified dimensions
        public Map( World _world, int _widthX, int _widthY, int _height )
            : this( _world ) {
            widthX = _widthX;
            widthY = _widthY;
            height = _height;

            int blockCount = widthX * widthY * height;

            blocks = new byte[blockCount];
            blocks.Initialize();
        }


        #region Saving
        public bool Save( string fileName ) {
            string tempFileName = fileName + "." + (new Random().Next().ToString());

            using( FileStream fs = File.Create( tempFileName ) ) {
                try {
                    WriteHeader( fs );
                    WriteMetadata( new BinaryWriter( fs ) );
                    changesSinceSave = 0;
                    GetCompressedCopy( fs, false );
                } catch( IOException ex ) {
                    Logger.Log( "Map.Save: Unable to open file \"{0}\" for writing: {1}", LogType.Error,
                                   tempFileName, ex.Message );
                    if( File.Exists( tempFileName ) ) {
                        File.Delete( tempFileName );
                    }
                    return false;
                }
            }

            try {
                if( File.Exists( fileName ) ) {
                    File.Delete( fileName );
                }
                File.Move( tempFileName, fileName );
                changesSinceBackup++;
                Logger.Log( "Saved map succesfully to {0}", LogType.SystemActivity, fileName );
            } catch( Exception ex ) {
                Logger.Log( "Error trying to replace " + fileName + ": " + ex.ToString() + ": " + ex.Message, LogType.Error );
                try {
                    if( File.Exists( tempFileName ) ) {
                        File.Delete( tempFileName );
                    }
                } catch( Exception ) { }
                return false;
            }
            return true;
        }


        void WriteHeader( FileStream fs ) {
            BinaryWriter writer = new BinaryWriter( fs );
            writer.Write( MapFCMv2.Identifier );
            writer.Write( (ushort)widthX );
            writer.Write( (ushort)widthY );
            writer.Write( (ushort)height );
            writer.Write( (ushort)spawn.x );
            writer.Write( (ushort)spawn.y );
            writer.Write( (ushort)spawn.h );
            writer.Write( (byte)spawn.r );
            writer.Write( (byte)spawn.l );
            writer.Flush();
        }


        internal void WriteMetadata( BinaryWriter writer ) {
            lock( metaLock ) {
                writer.Write( (ushort)( meta.Count + zones.Count ) );
                foreach( KeyValuePair<string, string> pair in meta ) {
                    WriteLengthPrefixedString( writer, pair.Key );
                    WriteLengthPrefixedString( writer, pair.Value );
                }
                int i = 0;
                lock( zoneLock ) {
                    foreach( Zone zone in zones.Values ) {
                        WriteLengthPrefixedString( writer, "@zone" + i );
                        WriteLengthPrefixedString( writer, zone.Serialize() );
                    }
                }
            }
            writer.Flush();
        }


        void WriteLengthPrefixedString( BinaryWriter writer, string s ) {
            byte[] stringData = ASCIIEncoding.ASCII.GetBytes( s );
            writer.Write( stringData.Length );
            writer.Write( stringData );
        }
        #endregion

        #region Loading
        public static Map Load( World _world, string fileName ) {
            Map map = null;

            // if file exists, go ahead and load
            if( File.Exists( fileName ) ) {
                map = DoLoad( fileName );

            // otherwise, try to append ".fcm"
            } else {
                if( File.Exists( fileName + ".fcm" ) ) {
                    map = DoLoad( fileName + ".fcm" );
                } else {
                    Logger.Log( "Map.Load: Could not find the specified file: {0}", LogType.Error, fileName );
                }
            }

            if( map != null ) {
                map.world = _world;
            }

            return map;
        }


        static Map DoLoad( string fileName ) {
            try {
                Map map = MapUtility.TryLoading( fileName );
                if( !map.ValidateBlockTypes( true ) ) {
                    throw new Exception( "Invalid block types detected. File is possibly corrupt." );
                }
                return map;

            } catch( EndOfStreamException ) {
                Logger.Log( "Map.Load: Unexpected end of file - possible corruption!", LogType.Error );
                return null;

            } catch( Exception ex ) {
                Logger.Log( "Map.Load: Error trying to read from \"{0}\": {1}", LogType.Error,
                            fileName,
                            ex.Message );
                return null;

            }
        }


        internal bool ValidateHeader() {
            if( !IsValidDimension( height ) ) {
                Logger.Log( "Map.ReadHeader: Invalid dimension specified for widthX: {0}.", LogType.Error, widthX );
                return false;
            }

            if( !IsValidDimension( widthY ) ) {
                Logger.Log( "Map.ReadHeader: Invalid dimension specified for widthY: {0}.", LogType.Error, widthY );
                return false;
            }

            if( !IsValidDimension( height ) ) {
                Logger.Log( "Map.ReadHeader: Invalid dimension specified for height: {0}.", LogType.Error, height );
                return false;
            }

            if( spawn.x > widthX * 32 || spawn.y > widthY * 32 || spawn.h > height * 32 || spawn.x < 0 || spawn.y < 0 || spawn.h < 0 ) {
                Logger.Log( "Map.ReadHeader: Spawn coordinates are outside the valid range! Using center of the map instead.", LogType.Warning );
                spawn.Set( widthX / 2 * 32, widthY / 2 * 32, height / 2 * 32, 0, 0 );
            }

            return true;
        }


        internal void ReadMetadata( BinaryReader reader ) {
            try {
                int metaSize = (int)reader.ReadUInt16();

                for( int i = 0; i < metaSize; i++ ) {
                    string key = ReadLengthPrefixedString( reader );
                    string value = ReadLengthPrefixedString( reader );
                    if( key.StartsWith( "@zone" ) ) {
                        try {
                            AddZone( new Zone( value ) );
                        } catch( Exception ex ) {
                            Logger.Log( "Map.ReadMetadata: cannot parse a zone: {0}", LogType.Error, ex.Message );
                        }
                    } else {
                        meta.Add( key, value );
                    }
                }

            } catch( FormatException ex ) {
                Logger.Log( "Map.ReadMetadata: Cannot parse one or more of the metadata entries: {0}", LogType.Error,
                            ex.Message );
            }
        }

        string ReadLengthPrefixedString( BinaryReader reader ) {
            int length = reader.ReadInt32();
            byte[] stringData = reader.ReadBytes( length );
            return ASCIIEncoding.ASCII.GetString( stringData );
        }


        // Only multiples of 16 are allowed, between 16 and 2032
        public static bool IsValidDimension( int dimension ) {
            return dimension > 0 && dimension % 16 == 0 && dimension < 2048;
        }
        #endregion

        #region Utilities
        static Dictionary<string, Block> blockNames = new Dictionary<string, Block>();
        static Map() {
            foreach( string block in Enum.GetNames( typeof( Block ) ) ) {
                blockNames.Add( block.ToLower(), (Block)Enum.Parse( typeof( Block ), block ) );
            }

            // alternative names for some blocks
            blockNames["none"] = Block.Air;
            blockNames["nothing"] = Block.Air;
            blockNames["empty"] = Block.Air;

            blockNames["soil"] = Block.Dirt;
            blockNames["stones"] = Block.Rocks;
            blockNames["cobblestone"] = Block.Rocks;
            blockNames["plank"] = Block.Wood;
            blockNames["planks"] = Block.Wood;
            blockNames["board"] = Block.Wood;
            blockNames["boards"] = Block.Wood;
            blockNames["tree"] = Block.Plant;
            blockNames["sappling"] = Block.Plant;
            blockNames["adminium"] = Block.Admincrete;
            blockNames["opcrete"] = Block.Admincrete;
            blockNames["ore"] = Block.IronOre;
            blockNames["coals"] = Block.Coal;
            blockNames["coalore"] = Block.Coal;
            blockNames["blackore"] = Block.Coal;

            blockNames["trunk"] = Block.Log;
            blockNames["stump"] = Block.Log;
            blockNames["treestump"] = Block.Log;
            blockNames["treetrunk"] = Block.Log;

            blockNames["leaf"] = Block.Leaves;
            blockNames["foliage"] = Block.Leaves;
            blockNames["grey"] = Block.Gray;
            blockNames["flower"] = Block.YellowFlower;

            blockNames["mushroom"] = Block.BrownMushroom;
            blockNames["shroom"] = Block.BrownMushroom;

            blockNames["iron"] = Block.Steel;
            blockNames["metal"] = Block.Steel;
            blockNames["silver"] = Block.Steel;

            blockNames["slab"] = Block.Stair;
            blockNames["slabs"] = Block.DoubleStair;
            blockNames["stairs"] = Block.DoubleStair;

            blockNames["bricks"] = Block.Brick;
            blockNames["explosive"] = Block.TNT;
            blockNames["dynamite"] = Block.TNT;

            blockNames["bookcase"] = Block.Books;
            blockNames["bookshelf"] = Block.Books;
            blockNames["bookshelves"] = Block.Books;
            blockNames["shelf"] = Block.Books;
            blockNames["shelves"] = Block.Books;
            blockNames["book"] = Block.Books;

            blockNames["moss"] = Block.MossyRocks;
            blockNames["mossy"] = Block.MossyRocks;
            blockNames["mossyrock"] = Block.MossyRocks;
            blockNames["mossystone"] = Block.MossyRocks;
            blockNames["mossystones"] = Block.MossyRocks;
        }


        internal static Block GetBlockByName( string block ) {
            return blockNames[block];
        }


        internal void CopyBlocks( byte[] source, int offset ) {
            blocks = new byte[widthX * widthY * height];
            Array.Copy( source, offset, blocks, 0, blocks.Length );
        }


        internal bool ValidateBlockTypes( bool returnOnErrors ) {
            for( int i = 0; i < blocks.Length; i++ ) {
                if( ( blocks[i] ) > 49 ) {
                    if( returnOnErrors ) return false;
                    else blocks[i] = 0;
                }
            }
            return true;
        }

        // zips a copy of the block array
        public void GetCompressedCopy( Stream stream, bool prependBlockCount ) {
            using( GZipStream compressor = new GZipStream( stream, CompressionMode.Compress ) ) {
                if( prependBlockCount ) {
                    // convert block count to big-endian
                    int convertedBlockCount = Server.SwapBytes( blocks.Length );
                    // write block count to gzip stream
                    compressor.Write( BitConverter.GetBytes( convertedBlockCount ), 0, sizeof( int ) );
                }
                compressor.Write( blocks, 0, blocks.Length );
            }
        }

        public void MakeFloodBarrier() {
            for( int x = 0; x < widthX; x++ ) {
                for( int y = 0; y < widthY; y++ ) {
                    SetBlock( x, y, 0, Block.Admincrete );
                }
            }

            for( int x = 0; x < widthX; x++ ) {
                for( int h = 0; h < height / 2; h++ ) {
                    SetBlock( x, 0, h, Block.Admincrete );
                    SetBlock( x, widthY - 1, h, Block.Admincrete );
                }
            }

            for( int y = 0; y < widthY; y++ ) {
                for( int h = 0; h < height / 2; h++ ) {
                    SetBlock( 0, y, h, Block.Admincrete );
                    SetBlock( widthX - 1, y, h, Block.Admincrete );
                }
            }
        }


        public int GetBlockCount() {
            return widthX * widthY * height;
        }

        #endregion

        #region Zones
        public Dictionary<string, Zone> zones = new Dictionary<string, Zone>();

        public bool AddZone( Zone z ) {
            lock( zoneLock ) {
                if( zones.ContainsKey( z.name.ToLower() ) ) return false;
                zones.Add( z.name.ToLower(), z );
                changesSinceSave++;
            }
            return true;
        }

        public bool RemoveZone( string z ) {
            lock( zoneLock ) {
                if( !zones.ContainsKey( z.ToLower() ) ) return false;
                zones.Remove( z.ToLower() );
                changesSinceSave++;
            }
            return true;
        }

        public Zone[] ListZones() {
            Zone[] output;
            int i = 0;
            lock( zoneLock ) {
                output = new Zone[zones.Count];
                foreach( Zone zone in zones.Values ) {
                    output[i++] = zone;
                }
            }
            return output;
        }

        // returns true if ANY zone intersects
        public bool CheckZones( short x, short y, short h, Player player, ref bool zoneOverride, ref string zoneName ) {
            bool found = false;
            lock( zoneLock ) {
                foreach( Zone zone in zones.Values ) {
                    if( zone.Contains( x, y, h ) ) {
                        found = true;
                        if( !zone.CanBuild( player ) ) {
                            zoneOverride = false;
                            zoneName = zone.name;
                            return true;
                        } else {
                            zoneOverride = true;
                        }
                    }
                }
            }
            return found;
        }


        public bool TestZones( short x, short y, short h, Player player, out Zone[] allowedZones, out Zone[] deniedZones ) {
            List<Zone> allowed = new List<Zone>(), denied = new List<Zone>();
            bool found = false;
            lock( zoneLock ) {
                foreach( Zone zone in zones.Values ) {
                    if( zone.Contains( x, y, h ) ) {
                        found = true;
                        if( zone.CanBuild( player ) ) {
                            allowed.Add( zone );
                        } else {
                            denied.Add( zone );
                        }
                    }
                }
            }
            allowedZones = allowed.ToArray();
            deniedZones = denied.ToArray();
            return found;
        }

        #endregion

        #region Block Updates & Simulation

        public int Index( int x, int y, int h ) {
            return ( h * widthY + y ) * widthX + x;
        }

        public void SetBlock( int x, int y, int h, Block type ) {
            if( x < widthX && y < widthY && h < height && x >= 0 && y >= 0 && h >= 0 )
                blocks[Index( x, y, h )] = (byte)type;
        }

        public void SetBlock( int x, int y, int h, byte type ) {
            if( x < widthX && y < widthY && h < height && x >= 0 && y >= 0 && h >= 0 && type < 50 )
                blocks[Index( x, y, h )] = type;
        }

        public byte GetBlock( int x, int y, int h ) {
            if( x < widthX && y < widthY && h < height && x >= 0 && y >= 0 && h >= 0 )
                return blocks[Index( x, y, h )];
            return 0;
        }

        internal void QueueUpdate( BlockUpdate update ) {
            lock( queueLock ) {
                updates.Enqueue( update );
            }
        }

        internal void PostQueueUpdate(BlockUpdate update)
        {
            lock (pqLock)
            {
                postupdates.Enqueue(update);
            }
        }



        internal void ClearUpdateQueue() {
            lock( queueLock ) {
                updates.Clear();
            }
        }


        public void ProcessUpdates() {
            int packetsSent = 0;
            int maxPacketsPerUpdate = Server.CalculateMaxPacketsPerUpdate( world );
            BlockUpdate update;
            while( updates.Count > 0 && packetsSent < maxPacketsPerUpdate ) {
                if( world.isLocked ) return;
                lock( queueLock ) {
                    update = updates.Dequeue();
                }
                changesSinceSave++;
                SetBlock( update.x, update.y, update.h, update.type );
                world.SendToAllDelayed( PacketWriter.MakeSetBlock( update.x, update.y, update.h, update.type ), update.origin );
                if( update.origin != null ) {
                    update.origin.info.ProcessBlockBuild( update.type );
                }
                packetsSent++;
            }

            if (updates.Count == 0 && world.isReadyForUnload == false)
            {
                if ((world.logicOn == true) || (world.physicsOn == true)) LogiProc();
                ProcessItemEntities();
            }

            if( updates.Count == 0 && world.isReadyForUnload ) {
                world.UnloadMap();
            }

            /*if( world.loadSendingInProgress ) { //TODO: streamload
                if( packetsSent < maxPacketsPerUpdate ) {
                    GC.Collect( GC.MaxGeneration, GCCollectionMode.Forced );
                    GC.WaitForPendingFinalizers();
                    world.SendToAll( PacketWriter.MakeMessage( Color.Red+"Map load complete." ), null );
                    Logger.Log( "Load command finished succesfully.", LogType.SystemActivity );
                    world.loadSendingInProgress = false;
                    //world.EndLockDown();
                } else {
                    if( !world.loadProgressReported && world.completedBlockUpdates / (float)world.totalBlockUpdates > 0.5f ) {
                        world.SendToAll( PacketWriter.MakeMessage( Color.Red + "Map loading: 50%" ), null );
                        world.loadProgressReported = true;
                    }
                    world.completedBlockUpdates += packetsSent;
                }
            }*/
        }


        public int CompareAndUpdate( Map other ) {
            int totalBlockUpdates = 0;
            int step = 8;
            for( int x = 0; x < widthX; x += step ) {
                for( int y = 0; y < widthY; y += step ) {
                    for( int h = 0; h < height; h++ ) {

                        for( int x2 = 0; x2 < step; x2++ ) {
                            for( int y2 = 0; y2 < step; y2++ ) {
                                int index = Index( x + x2, y + y2, h );
                                if( blocks[index] != other.blocks[index] ) {
                                    QueueUpdate( new BlockUpdate( null, x + x2, y + y2, h, other.blocks[index] ) );
                                    totalBlockUpdates++;
                                }
                            }
                        }

                    }
                }
            }
            return totalBlockUpdates;
        }

        internal int GetQueueLength() {
            return updates.Count;
        }
        #endregion

        #region Asiekierka's Block Modification Handler (ABloMoHa)

        #region Utilities
        public byte GetBlockA(int x, int y, int h)
        {
            if (x < widthX && y < widthY && h < height && x >= 0 && y >= 0 && h >= 0)
                return blocks[Index(x, y, h)];
            return 255;
        }

        public byte GetBlockAH(int x, int y, int h)
        {
            if (h < height && h >= 0)
                return blocks[Index(x, y, h)];
            return 255;
        }
        internal int BlockScanM(int ox, int oz, int oy, byte ob, bool CheckDiag, bool CheckSelf)
        {
            int oi = 0;
            if ((CheckSelf == true) && (GetBlockA(ox, oz, oy) == ob)) { oi++; }
            if (GetBlockA(ox, oz - 1, oy) == ob) { oi++; }
            if (GetBlockA(ox - 1, oz, oy) == ob) { oi++; }
            if (GetBlockA(ox + 1, oz, oy) == ob) { oi++; }
            if (GetBlockA(ox, oz + 1, oy) == ob) { oi++; }
            if (CheckDiag == true)
            {
                if (GetBlockA(ox - 1, oz - 1, oy) == ob) { oi++; }
                if (GetBlockA(ox + 1, oz - 1, oy) == ob) { oi++; }
                if (GetBlockA(ox - 1, oz + 1, oy) == ob) { oi++; }
                if (GetBlockA(ox + 1, oz + 1, oy) == ob) { oi++; }
            }
            return oi;
        }
        internal void PostQueueProcess()
        {
            lock (pqLock)
            {
                lock (queueLock)
                {
                    while (postupdates.Count > 0)
                    {
                        BlockUpdate pubu = postupdates.Dequeue();
                        updates.Enqueue(pubu);
                    }
                }
            }
        }
        internal void TNTExplode(int ox, int oz, int oy)
        {
            tntpl.Add(new Position((short)ox, (short)oz, (short)oy));
            for (int sx = -2; sx < 3; sx++)
            {
                for (int sz = -2; sz < 3; sz++)
                {
                    for (int sy = -2; sy < 3; sy++)
                    {
                        if ((sx != 0 || sz != 0 || sy != 0) && GetBlockA(ox + sx, oz + sz, oy + sy) == 46)
                        {
                            lock (tntplock)
                            {
                                int i = 0;
                                foreach (Position tp in tntpl)
                                {
                                    if (tp.x == ox+sx && tp.y == oz+sz && tp.h == oy+sy) i++;
                                }
                                if (i == 0)
                                {
                                    TNTExplode(ox + sx, oz + sz, oy + sy);
                                }
                            }
                        }
                        else if (GetBlockA(ox + sx, oz + sz, oy + sy) != 7)
                        {
                            PostQueueUpdate(new BlockUpdate(null, ox + sx, oz + sz, oy + sy, 0));
                        }
                    }
                }
            }
        }
        internal void TNTExplodeWW(int ox, int oz, int oy)
        {
            byte tb;
            tntpl.Add(new Position((short)ox, (short)oz, (short)oy));
            for (int sx = -2; sx < 3; sx++)
            {
                for (int sz = -2; sz < 3; sz++)
                {
                    for (int sy = -2; sy < 3; sy++)
                    {
                        tb = GetBlockA(ox + sx, oz + sz, oy + sy);
                        if ((sx != 0 || sz != 0 || sy != 0) && tb == 46)
                        {
                            lock (tntplock)
                            {
                                int i = 0;
                                foreach (Position tp in tntpl)
                                {
                                    if (tp.x == ox + sx && tp.y == oz + sz && tp.h == oy + sy) i++;
                                }
                                if (i == 0)
                                {
                                    TNTExplode(ox + sx, oz + sz, oy + sy);
                                }
                            }
                        }
                        else if (tb != 7 && tb != 21 && tb != 23 && tb != 34)
                        {
                            PostQueueUpdate(new BlockUpdate(null, ox + sx, oz + sz, oy + sy, 0));
                        }
                    }
                }
            }
        }
        #endregion
        #region WireWorld utils
        internal int LogiScan(int ox, int oz, int oy, bool CheckSelf)
        {
            int oi = 0;
            if ((CheckSelf == true) && (GetBlockA(ox, oz, oy) == 21)) { oi++; }
            if (GetBlockA(ox - 1, oz - 1, oy) == 21) { oi++; }
            if (GetBlockA(ox, oz - 1, oy) == 21) { oi++; }
            if (GetBlockA(ox + 1, oz - 1, oy) == 21) { oi++; }
            if (GetBlockA(ox - 1, oz, oy) == 21) { oi++; }
            if (GetBlockA(ox + 1, oz, oy) == 21) { oi++; }
            if (GetBlockA(ox - 1, oz + 1, oy) == 21) { oi++; }
            if (GetBlockA(ox, oz + 1, oy) == 21) { oi++; }
            if (GetBlockA(ox + 1, oz + 1, oy) == 21) { oi++; }
            return oi;
        }
        #endregion

        static Random rand = new Random();
        long LastLavaTime = -1;

        #region Finite physics handlers
        internal bool FPIsNotBlocked(int ix, int iz, int iy, byte it)
        {
            if (GetBlockA(ix, iz, iy) == 0)
            {
                return true;
            }
            else if (((world.blockFlag[it] & 32) > 0) && GetBlockA(ix, iz, iy) == 8)
            {
                return true;
            }
            else if (((world.blockFlag[it] & 64) > 0) && GetBlockA(ix, iz, iy) == 10)
            {
                return true;
            }
            return false;
        }
        internal bool PlaceFPBlock(int ox, int oz, int oy, int ix, int iz, int iy, byte it)
        {
            if (GetBlockA(ix, iz, iy) == 0)
            {
                QueueUpdate(new BlockUpdate(null, ix, iz, iy, it));
                QueueUpdate(new BlockUpdate(null, ox, oz, oy, 0));
                return true;
            }
            else if (((world.blockFlag[it] & 32) > 0) && GetBlockA(ix, iz, iy) == 8)
            {
                switch(world.modeWater)
                {
                    case 2: // finite water
                        QueueUpdate(new BlockUpdate(null, ix, iz, iy, it));
                        QueueUpdate(new BlockUpdate(null, ox, oz, oy, 8));
                        return true;
                    default: // infinite water
                        if ((GetBlockA(ox - 1, oz, oy) == 8) || (GetBlockA(ox + 1, oz, oy) == 8) || (GetBlockA(ox, oz - 1, oy) == 8) || (GetBlockA(ox, oz + 1, oy) == 8))
                        {
                            QueueUpdate(new BlockUpdate(null, ix, iz, iy, it));
                            QueueUpdate(new BlockUpdate(null, ox, oz, oy, 8));
                        }
                        else
                        {
                            QueueUpdate(new BlockUpdate(null, ix, iz, iy, it));
                            QueueUpdate(new BlockUpdate(null, ox, oz, oy, 0));
                        }
                        return true;
                }
            }
            else if (((world.blockFlag[it] & 64) > 0) && GetBlockA(ix, iz, iy) == 10)
            {
                switch (world.modeWater)
                {
                    case 2: // finite lava
                        QueueUpdate(new BlockUpdate(null, ix, iz, iy, it));
                        QueueUpdate(new BlockUpdate(null, ox, oz, oy, 10));
                        return true;
                    default: // infinite lava
                        if ((GetBlockA(ox - 1, oz, oy) == 10) || (GetBlockA(ox + 1, oz, oy) == 10) || (GetBlockA(ox, oz - 1, oy) == 10) || (GetBlockA(ox, oz + 1, oy) == 10))
                        {
                            QueueUpdate(new BlockUpdate(null, ix, iz, iy, it));
                            QueueUpdate(new BlockUpdate(null, ox, oz, oy, 10));
                        }
                        else
                        {
                            QueueUpdate(new BlockUpdate(null, ix, iz, iy, it));
                            QueueUpdate(new BlockUpdate(null, ox, oz, oy, 0));
                        }
                        return true;
                }
            }
            return false;
        }
        #endregion
        #region Water/sponge support
        internal bool PlaceFPWBlock(int ox, int oz, int oy, int ix, int iz, int iy, byte it)
        {
            // TODO: make it proper and faster
            for (int sx = -2; sx < 3; sx++)
            {
                for (int sz = -2; sz < 3; sz++)
                {
                    for (int sy = -2; sy < 3; sy++)
                    {
                        if (GetBlockA(ix + sx, iz + sz, iy + sy) == 19)
                        {
                            return false;
                        }
                    }
                }
            }
            return PlaceFPBlock(ox, oz, oy, ix, iz, iy, it);
        }
        internal bool PlaceWater(int ox, int oz, int oy)
        {
            byte ibb = GetBlockA(ox, oz, oy);
            if (ibb != 0) return false;
            return true;
        }
        internal bool CheckSpongeXZ(int ox, int oz, int oy)
        {
            for (int sx = -2; sx < 3; sx++)
            {
                for (int sz = -2; sz < 3; sz++)
                {
                     if (GetBlockA(ox + sx, oz + sz, oy) == 19)
                    {
                        return false;
                    }
                }
            }
            return true;
        }
        internal bool CheckSpongeXY(int ox, int oz, int oy)
        {
            for (int sx = -2; sx < 3; sx++)
            {
                for (int sy = -2; sy < 3; sy++)
                {
                    if (GetBlockA(ox + sx, oz, oy+sy) == 19)
                    {
                        return false;
                    }
                }
            }
            return true;
        }
        internal bool CheckSpongeYZ(int ox, int oz, int oy)
        {
            for (int sz = -2; sz < 3; sz++)
            {
                for (int sy = -2; sy < 3; sy++)
                {
                    if (GetBlockA(ox, oz + sz, oy + sy) == 19)
                    {
                        return false;
                    }
                }
            }
            return true;
        }
        #endregion
        public void LogiProc()
        {
            #region LogiProc - lava delay
            Boolean tempPON = false;
            long tempPT = DateTime.UtcNow.Ticks / 100000;
            if (world.physicsOn == true)
            {
                if (LastLavaTime == -1) { LastLavaTime = tempPT; tempPON = true; }
                if (LastLavaTime == 0) { LastLavaTime = 100; }
                if (tempPT - LastLavaTime >= 45) { tempPON = true; LastLavaTime = tempPT; }
                if (world.blockFlag == null) { world.blockFlag = new byte[50]; }
            }
            #endregion
            // just needed
            world.blockFlag[8] = 0;
            world.blockFlag[10] = 0;
            for (int iy = height - 1; iy >= 0; iy--)
            {
                for (int iz = widthY - 1; iz >= 0; iz--)
                {
                    for (int ix = widthX - 1; ix >= 0; ix--)
                    {
                        // Now we go over each and every block.
                        byte ib = GetBlock(ix, iz, iy);
                        #region LogiProc - WireWorld
                        if (world.logicOn == true) switch (ib)
                            {
                                case 21: QueueUpdate(new BlockUpdate(null, ix, iz, iy, 34)); break;
                                case 34: QueueUpdate(new BlockUpdate(null, ix, iz, iy, 23)); break;
                                case 23: int i = LogiScan(ix, iz, iy, false);
                                    if (world.logicOn3D == true)
                                    {
                                        i += LogiScan(ix, iz, iy - 1, true);
                                        i += LogiScan(ix, iz, iy + 1, true);
                                    }
                                    if ((i > 0) && (i < 3)) { QueueUpdate(new BlockUpdate(null, ix, iz, iy, 21)); }
                                    break;
                                // TNT!
                                case 46: int ti = BlockScanM(ix, iz, iy, 21, false, false);
                                    if (ti > 0)
                                    {
                                        tntpl.Clear();
                                        TNTExplodeWW(ix, iz, iy);
                                    }
                                    break;
                                default: break;
                            }
                        #endregion
                        if ((world.physicsOn == true) && ((tempPON == true) || (ib != 10) || (world.blockFlag[ib] == 1)))
                        {
                            byte tv = 255;
                            switch (ib)
                            {
                                #region LogiProc - Water
                                case 8:
                                    for (int sx = -2; sx < 3; sx++)
                                    {
                                        for (int sz = -2; sz < 3; sz++)
                                        {
                                            for (int sy = -2; sy < 3; sy++)
                                            {
                                                if (GetBlockA(ix + sx, iz + sz, iy + sy) == 19)
                                                {
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                    switch (world.modeWater)
                                    {
                                        case 1:
                                            if (PlaceWater(ix, iz, iy - 1) && CheckSpongeXZ(ix,iz,iy-3)) { QueueUpdate(new BlockUpdate(null, ix, iz, iy - 1, ib)); }
                                            if (rand.Next(0, 1000) < 801)
                                            {
                                                if (PlaceWater(ix - 1, iz, iy) && CheckSpongeYZ(ix-3, iz, iy)) { QueueUpdate(new BlockUpdate(null, ix - 1, iz, iy, ib)); }
                                                if (PlaceWater(ix + 1, iz, iy) && CheckSpongeYZ(ix+3, iz, iy)) { QueueUpdate(new BlockUpdate(null, ix + 1, iz, iy, ib)); }
                                                if (PlaceWater(ix, iz - 1, iy) && CheckSpongeXY(ix, iz-3, iy)) { QueueUpdate(new BlockUpdate(null, ix, iz - 1, iy, ib)); }
                                                if (PlaceWater(ix, iz + 1, iy) && CheckSpongeXY(ix, iz+3, iy)) { QueueUpdate(new BlockUpdate(null, ix, iz + 1, iy, ib)); }
                                            }
                                            break;
                                        case 2:
                                            if (PlaceFPWBlock(ix, iz, iy, ix, iz, iy - 1, ib))
                                            {
                                                break;
                                            }
                                            else
                                            {
                                                int xt = rand.Next(0, 255) % 4;
                                                int xstep = 0;
                                                int ystep = 0;
                                                switch (xt)
                                                {
                                                    case 0: xstep = 0; ystep = -1; break;
                                                    case 1: xstep = 1; ystep = 0; break;
                                                    case 2: xstep = 0; ystep = 1; break;
                                                    case 3: xstep = -1; ystep = 0; break;
                                                }
                                                if (GetBlockA(ix+xstep, iz+ystep, iy) == 0)
                                                {
                                                    if (PlaceFPWBlock(ix, iz, iy, ix + xstep, iz + ystep, iy - 1, ib))
                                                    {
                                                        break;
                                                    }
                                                    else if (rand.Next(0,63)==21)
                                                    {
                                                        PlaceFPWBlock(ix, iz, iy, ix + xstep, iz + ystep, iy, ib);
                                                        break;
                                                    }
                                                }
                                            }
                                            break;
                                        default: break;
                                    }
                                    break;
                                #endregion
                                #region LogiProc - Lava
                                case 10: // Wava!
                                    switch (world.modeWater)
                                    {
                                        case 1:
                                            if (PlaceWater(ix, iz, iy - 1)) { QueueUpdate(new BlockUpdate(null, ix, iz, iy - 1, ib)); }
                                            if (rand.Next(0, 1000) < 801)
                                            {
                                                if (PlaceWater(ix - 1, iz, iy)) { QueueUpdate(new BlockUpdate(null, ix - 1, iz, iy, ib)); }
                                                if (PlaceWater(ix + 1, iz, iy)) { QueueUpdate(new BlockUpdate(null, ix + 1, iz, iy, ib)); }
                                                if (PlaceWater(ix, iz - 1, iy)) { QueueUpdate(new BlockUpdate(null, ix, iz - 1, iy, ib)); }
                                                if (PlaceWater(ix, iz + 1, iy)) { QueueUpdate(new BlockUpdate(null, ix, iz + 1, iy, ib)); }
                                            }
                                            break;
                                        case 2:
                                            if (PlaceFPBlock(ix, iz, iy, ix, iz, iy - 1, ib))
                                            {
                                                break;
                                            }
                                            else
                                            {
                                                int xt = rand.Next(0, 255) % 4;
                                                int xstep = 0;
                                                int ystep = 0;
                                                switch (xt)
                                                {
                                                    case 0: xstep = 0; ystep = -1; break;
                                                    case 1: xstep = 1; ystep = 0; break;
                                                    case 2: xstep = 0; ystep = 1; break;
                                                    case 3: xstep = -1; ystep = 0; break;
                                                }
                                                if (GetBlockA(ix + xstep, iz + ystep, iy) == 0)
                                                {
                                                    if (PlaceFPBlock(ix, iz, iy, ix + xstep, iz + ystep, iy - 1, ib))
                                                    {
                                                        break;
                                                    }
                                                    else if (rand.Next(0, 95) == 24)
                                                    {
                                                        PlaceFPBlock(ix, iz, iy, ix + xstep, iz + ystep, iy, ib);
                                                        break;
                                                    }
                                                }
                                            }
                                            break;
                                        default: break;
                                    }
                                    break;
                                #endregion
                                #region LogiProc - Sponge
                                case 19: // Sponges
                                    for (int sx = -2; sx < 3; sx++)
                                    {
                                        for (int sz = -2; sz < 3; sz++)
                                        {
                                            for (int sy = -2; sy < 3; sy++)
                                            {
                                                if (GetBlockA(ix + sx, iz + sz, iy + sy) == 8)
                                                {
                                                    PostQueueUpdate(new BlockUpdate(null, ix + sx, iz + sz, iy + sy, 0));
                                                }
                                            }
                                        }
                                    }
                                    break;
                                #endregion
                                #region LogiProc - Grass (lacking)
                                case 3: // Dirt
                                    tv = GetBlock(ix, iz, iy + 1);
                                    if (tv == 0 || tv == 18 || tv == 20) QueueUpdate(new BlockUpdate(null, ix, iz, iy, 2));
                                    break;
                                case 2: // Grass
                                    tv = GetBlock(ix, iz, iy + 1);
                                    if (tv != 0 && tv != 18 && tv != 20) QueueUpdate(new BlockUpdate(null, ix, iz, iy, 3));
                                    break;
                                #endregion
                                #region LogiProc - TNT (physics)
                                case 46: int ti = BlockScanM(ix, iz, iy, 10, false, false);
                                    if (ti > 0)
                                    {
                                        tntpl.Clear();
                                        TNTExplode(ix, iz, iy);
                                    }
                                    break;
                                #endregion
                            }
                                #region LogiProc - finite physics
                                if ((world.blockFlag[ib]&1) == 1)
                                    {
                                        if (PlaceFPBlock(ix,iz,iy,ix,iz,iy-1,ib))
                                        {
                                            if (ib == 46 && GetBlockA(ix,iz,iy-2) > 0)
                                            {
                                                tntpl.Clear();
                                                TNTExplode(ix, iz, iy - 1);
                                            }
                                        }
                                        else
                                        {
                                            int xt = rand.Next(0, 255) % 4;
                                            int xstep = 0;
                                            int ystep = 0;
                                            switch (xt)
                                            {
                                                case 0: xstep = 0; ystep = -1; break;
                                                case 1: xstep = 1; ystep = 0; break;
                                                case 2: xstep = 0; ystep = 1; break;
                                                case 3: xstep = -1; ystep = 0; break;
                                            }
                                            if (FPIsNotBlocked(ix + xstep, iz + ystep, iy, ib))
                                            {
                                                if (PlaceFPBlock(ix, iz, iy, ix + xstep, iz + ystep, iy - 1, ib))
                                                {
                                                    if (ib == 46 && GetBlockA(ix+xstep, iz+ystep, iy - 2) > 0)
                                                    {
                                                        tntpl.Clear();
                                                        TNTExplode(ix+xstep, iz+ystep, iy - 1);
                                                    }
                                                }
                                                else if (((world.blockFlag[ib] & 2) > 0) && (rand.Next(0, 31) < ((world.blockFlag[ib] & 28) + 3)))
                                                {
                                                    PlaceFPBlock(ix, iz, iy, ix + xstep, iz + ystep, iy, ib);
                                                }
                                            }
                                        }
                                    }
                                #endregion
                        }
                    }
                }
            }
            PostQueueProcess();
        }
        #endregion

        #region ItemEntity handling

        internal static ItemEntType GetItemEntityByName(string ien)
        {
            ItemEntity tmpie = new ItemEntity(1);
            return tmpie.GetIEByName(ien);
        }
        public void AddItemEntity(ItemEntity ie)
        {
            lock (ietlock)
            {
                ietlist.Add(ie);
            }
        }
        public bool AddItemEntity(ItemEntity ie, string[] ietp)
        {
            ItemEntity tmpie = ie;
            switch (ie.type)
            {
                case 3:
                    byte i = 255;
                    try { i = Convert.ToByte(ietp[0]); }
                    catch { try { i = (byte)GetBlockByName(ietp[0]); } catch { return false; } }
                    if (i > 0 && i <= 49)
                        ie.SetByte(0, i);
                    else return false;
                    break;
                default: break;
            }
            lock (ietlock)
            {
                ietlist.Add(tmpie);
            }
            return true;
        }

        public void ClearItemEntities()
        {
            lock (ietlock)
            {
                ietlist.Clear();
            }
        }

        internal void ProcessItemEntities()
        {
            lock (ietlock)
            {
                foreach (ItemEntity pIEnt in ietlist)
                {
                    /*
                    if (pIEnt.blocktype <= 49 && GetBlockA(pIEnt.x,pIEnt.y,pIEnt.h) != pIEnt.blocktype)
                    {
                        QueueUpdate(new BlockUpdate(null, pIEnt.x, pIEnt.y, pIEnt.h, pIEnt.blocktype));
                    }
                    */
                    int ix = pIEnt.x;
                    int iy = pIEnt.y;
                    int ih = pIEnt.h;
                    int it = pIEnt.type;
                    switch (it)
                    {
                        case 0: break;
                        case 1: if (GetBlockA(ix, iy, ih+1) == 0)
                            {
                                QueueUpdate(new BlockUpdate(null,ix, iy, ih+1,8));
                            }
                            break;
                        case 2: if (GetBlockA(ix, iy, ih+1) == 0)
                            {
                                QueueUpdate(new BlockUpdate(null, ix, iy, ih+1, 10));
                            }
                            break;
                        case 3: if (GetBlockA(ix, iy, ih + 1) == 0)
                            {
                                QueueUpdate(new BlockUpdate(null, ix, iy, ih + 1, pIEnt.GetByte(0)));
                            }
                            break;
                        default: break;
                    }
                }
            }
        }

        #endregion
        #region Backup
        public void SaveBackup( string sourceName, string targetName ) {
            if( changesSinceBackup == 0 && Config.GetBool( ConfigKey.BackupOnlyWhenChanged ) ) return;
            if( !Directory.Exists( "backups" ) ) {
                Directory.CreateDirectory( "backups" );
            }
            changesSinceBackup = 0;
            File.Copy( sourceName, targetName, true );
            FileInfo[] info = new DirectoryInfo( "backups" ).GetFiles();
            Array.Sort<FileInfo>( info, FileInfoComparer.instance );
            Queue<string> files = new Queue<string>();
            for( int i = 0; i < info.Length; i++ ) {
                if( info[i].Extension == ".fcm" ) {
                    files.Enqueue( info[i].Name );
                }
            }
            if( Config.GetInt( ConfigKey.MaxBackups ) > 0 ) {
                while( files.Count > Config.GetInt( ConfigKey.MaxBackups ) ) {
                    File.Delete( "backups/" + files.Dequeue() );
                }
            }
            Logger.Log( "AutoBackup: " + targetName, LogType.SystemActivity );
        }

        sealed class FileInfoComparer : IComparer<FileInfo> {
            public static FileInfoComparer instance = new FileInfoComparer();
            public int Compare( FileInfo x, FileInfo y ) {
                return x.CreationTime.CompareTo( y.CreationTime );
            }
        }
        #endregion
    }
}