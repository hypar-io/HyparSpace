# HyparSpace

## About
This repository contains the source code to a range of functions for doing spatial layouts. There are two main groups: **Zone Planning Functions**, which intake building geometry and produce Space Boundaries, and **Layout Functions**, which intake Space Boundaries and produce furniture layouts.


* ### Zone Planning Functions
  * Space Planning (`Function-SpacePlanning`) (note: this is a submodule)
  * Circulation (`Function-Circulation`)  (note: this is a submodule)
  * Archive /
    * Circulation (an older version of the circulation function)
    * Space Planning Zones (an older version of Space Planning)
    * Space Planning Zones Auto-Place
* ### Layout Functions
  * Open Office Layout
  * Private Office Layout
  * Pantry Layout
  * Reception Layout
  * Classroom Layout
  * Meeting Room Layout
  * Phone Booth Layout
  * Lounge Layout


## To develop your own function based on one of these:
1. Duplicate the folder containing the function you want to base yours on
2. **Important!!** Edit its `hypar.json` file to replace the value of the `id` property with a new GUID. You will not be able to publish or test otherwise! Be sure to save the file.
3. Use the `hypar rename` command to give the function a different name
4. Use Hypar run / hypar publish as normal.