#ifndef HYPERMANIA_FIGHTER_SILHOUETTES_INCLUDED
#define HYPERMANIA_FIGHTER_SILHOUETTES_INCLUDED

// One screen-sized texture per player with only that fighter drawn into it,
// in full colour with alpha as coverage. Written every frame by
// FighterSilhouetteCapture.
TEXTURE2D(_FighterSilhouette0);
TEXTURE2D(_FighterSilhouette1);
SAMPLER(sampler_FighterSilhouette0);

#endif
