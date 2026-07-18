namespace Leostrap.Enums.FlagPresets
{
    public enum RenderingMode
    {
        [EnumName(FromTranslation = "Common.Automatic")]
        Automatic,

        [EnumName(StaticName = "Direct3D 11")]
        D3D11,

        [EnumName(StaticName = "Vulkan")]
        Vulkan,

        [EnumName(StaticName = "OpenGL")]
        OpenGL
    }
}
