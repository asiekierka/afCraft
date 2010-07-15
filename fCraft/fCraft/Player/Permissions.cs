using System;

namespace fCraft {
    public enum Permissions {
        Chat,
        PrivateChat,
        Build,
        Delete,

        PlaceGrass,
        PlaceWater, // includes placing water blocks
                    // changing water sim parameters is in ControlPhysics
        PlaceLava,  // same as above, but with lava
        PlaceAdmincrete,  // build admincrete
        DeleteAdmincrete, // delete admincrete
        PlaceHardenedBlocks, // Place all blocks as admincrete
        PlaceItemEnt, // place item entities
        PlaceRealWater,
        PlaceRealLava,
        DestroyTNT,

        Say,
        Kick,
        Ban,
        BanIP,
        BanAll,
        SeeLowerClassChat,
       
        Promote,
        Demote,
        Hide,         // go invisible!
        ChangeName,   // change own name

        ViewOthersInfo,

        Teleport,
        Bring,
        Freeze,
        SetSpawn,
        Lock,

        SwitchLogic,

        ControlPhysics,

        AddLandmarks,

        ManageZones,
        ManageWorlds,

        Draw
    }
}
