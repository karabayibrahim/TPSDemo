# Project asset layout

Project-owned content lives in `Assets/Game`. Imported packages retain their own layouts; Kenney source models and licenses are in `Assets/ThirdParty/Kenney`.

| Folder | Contents |
| --- | --- |
| Scripts/CityGeneration/Core | OSM parsing, data types, city orchestration |
| Scripts/CityGeneration/Buildings | Building meshes and interior mapping data |
| Scripts/CityGeneration/Roads | Roads, intersections, sidewalks and road network |
| Scripts/CityGeneration/Terrain | Terrain generation, landscape painting and surface conforming |
| Scripts/CityGeneration/Props | Street prop placement |
| Scripts/Lighting | Day/night cycle and street light management |
| Scripts/Camera | Cinematic camera |
| Editor | Inspector tools and street prop prefab builder |
| Shaders | Buildings, roads, lighting and street prop shaders |
| Materials | Building facades/roofs/glass, roads, terrain, lighting and street props |
| Materials/Legacy | Existing material experiments, preserved with their references |
| Textures | Building, road and terrain textures; older building textures under Legacy |
| Models/StreetProps | Generated combined street prop meshes |
| Models/Imported | Previously loose imported models |
| Prefabs/StreetProps | Reusable street furniture and parked vehicle prefabs |
| Terrain/Layers | TerrainLayer assets |
| Data/OSM | Source OSM map |
| Input | Input actions |
| Reference | Reference images |

Move Unity assets through the Project window to preserve their GUIDs. Keep `.meta` files with their assets. `Tools > City > Build Street Prop Prefabs` writes to the organized material, model and prefab folders.
