

# Circulation

Generate building circulation. Automatically generate or draw corridors. Note that when drawing, corridors will propagate to all levels, unless you are in a specific floor plan view.

|Input Name|Type|Description|
|---|---|---|
|Circulation Mode|string|How should circulation be calculated? 
Automatic: a typical circulation network will be generated for you. 
Manual: you draw the circulation paths yourself.|
|Corridor Width|number|How wide should circulation paths be?|
|Outer Band Depth|number|For the "outer band" of program running along the floor perimeter, how deep should the spaces be?|
|Depth at Ends|number|If your floorplate is rectangular, or has roughly rectangular ends, how deep should the spaces be at these ends?|
|Add Corridors|array|Insert additional corridors, to further subdivide the space.|


<br>

|Output Name|Type|Description|
|---|---|---|

