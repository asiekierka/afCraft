using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace fCraft
{
    public enum ItemEntType : int
    {
        Null = 0,
        WaterGen = 1,
        LavaGen = 2,
    }

    public struct ItemEntity
    {
        public int ietlen;
        public int type; // type of the block
        public byte blocktype; // just so it is there
        public int x, y, h; // position of the entity
        public byte[] data;
        static Dictionary<string, ItemEntType> ieNames = new Dictionary<string, ItemEntType>();
        static Dictionary<int, byte> ieBTs = new Dictionary<int, byte>();

        #region Initalization
        internal void InitTables()
        {
            foreach( string block in Enum.GetNames( typeof( ItemEntType ) ) ) {
                ieNames.Add( block.ToLower(), (ItemEntType)Enum.Parse( typeof( ItemEntType ), block ) );
            }

            ieBTs[0] = 0;
            ieBTs[1] = (byte)Block.Aqua;
            ieBTs[2] = (byte)Block.Orange;

            // If I get lazy, this will fix my laziness
            if(ietlen > 2)
                for (int i = 3; i <= ietlen; i++)
                {
                    ieBTs[i] = 1;
                }
        }
        public ItemEntity(int ax, int ay, int ah, int aty)
        {
            type = aty;
            x = ax;
            y = ay;
            h = ah;
            blocktype = 0; // ugly hack because Visual C# 2010 Express is a fat Micro$ jerk
            data = new byte[128];
            ietlen = 2;
            InitTables();  // yes, i use Visual C# 2010 Express... don't ask. no, don't.
            blocktype = ieBTs[type];
        }
        public ItemEntity(int ax, int ay, int ah, int aty, byte abt)
        {
            type = aty;
            x = ax;
            y = ay;
            h = ah;
            blocktype = abt; //  DEVELOPERS WORLDWIDE REJOICE AS
            data = new byte[128];
            ietlen = 2;
            InitTables();    // ASIEKIERKA REMOVES THE UGLY HACK!
        }
        #endregion

        #region Getting/Setting variables
        public int GetInt(byte position)
        {
            if (position <= data.Length - 4)
            {
                return data[position] << 24 | data[position + 1] << 16 | data[position + 2] << 8 | data[position + 3];
            }
            return -1;
        }
        public short GetShort(byte position)
        {
            if (position <= data.Length - 2)
            {
                return (short)(data[position] << 8 | data[position + 1]);
            }
            return -1;
        }

        public bool SetInt(byte position, int value)
        {
            if (value < 0 || position > data.Length - 4)
            {
                return false;
            }
            try
            {
                data[position] = (byte)((value>>24)&255);
                data[position+1] = (byte)((value>>16)&255);
                data[position+2] = (byte)((value>>8)&255);
                data[position+3] = (byte)(value&255);
            }
            catch { return false; }
            return true;
        }
        public bool SetShort(byte position, short value)
        {
            if (value < 0 || position > data.Length - 2)
            {
                return false;
            }
            try
            {
                data[position] = (byte)((value >> 8) & 255);
                data[position + 1] = (byte)(value & 255);
            }
            catch { return false; }
            return true;
        }

        // I'm so nice I give you GetByte and SetByte even if you dont need them!
        public byte GetByte(byte position)
        {
            return data[position];
        }
        public bool SetByte(byte position, byte value)
        {
            try
            {
                data[position] = value;
            }
            catch { return false; }
            return true;
        }
        #endregion

        #region Name handling stuff
        internal static ItemEntType GetIEByName(string iet)
        {
            return ieNames[iet.ToLower()];
        }
        #endregion
    }
}
