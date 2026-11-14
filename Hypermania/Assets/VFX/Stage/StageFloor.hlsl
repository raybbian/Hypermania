#ifndef HYPERMANIA_STAGE_FLOOR_INCLUDED
#define HYPERMANIA_STAGE_FLOOR_INCLUDED

// Published by StageFloor.cs when a stage loads.
float4 _StageFloor;

// World height of the ground line the fighters stand on.
#define STAGE_GROUND_Y _StageFloor.x
// World height where the floor ends and the back wall begins.
#define STAGE_FLOOR_TOP_Y _StageFloor.y

#endif
