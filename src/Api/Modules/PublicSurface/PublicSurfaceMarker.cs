namespace Api.Modules.PublicSurface;

// Anchor for the /public/* surface (design.md §3); endpoints land in slice 1.5.
// This namespace must never reference INext3Client or user/profile types (arch rule 2).
public static class PublicSurfaceMarker
{
}
