

# Space Planning Zones

Generate a circulation path and high-level zones for a schematic space plan.

|Input Name|Type|Description|
|---|---|---|
|Default Program Assignment|string|What would you like the default program for all zones to be? This program type will be assigned to all spaces, and then you can pick specific programs for individual spaces with the Edit Program Assignments button.|
|Circulation Mode|string|How should circulation be calculated? 
Automatic: a typical circulation network will be generated for you. 
Manual: you draw the circulation paths yourself.|
|Corridors|array|Define the circulation network by drawing one or more corridor paths.|
|Corridor Width|number|How wide should circulation paths be?|
|Outer Band Depth|number|For the "outer band" of program running along the floor perimeter, how deep should the spaces be?|
|Depth at Ends|number|If your floorplate is rectangular, or has roughly rectangular ends, how deep should the spaces be at these ends?|
|Additional Corridor Locations|array|Add new points to this list to insert additional corridor locations, to further subdivide the space. Corridors extend perpendicularly from the closest point on the boundary.|
|Manual Split Locations|array|Add new points to this list to insert additional program split locations, to further subdivide the space. This is similar to the corridor locations input above, but does not insert circulation between split spaces.|


<br>

|Output Name|Type|Description|
|---|---|---|

