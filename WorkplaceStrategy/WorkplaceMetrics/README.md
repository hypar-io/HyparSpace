

# Workplace Metrics

Calculate various workplace metrics from a layout.

|Input Name|Type|Description|
|---|---|---|
|Calculation Mode|string||
|Total Headcount|integer|How many people will occupy this workspace?|
|Desk Sharing Ratio|number|What is the assumed sharing ratio: How many people for every desk? A value of 1 means one desk for every person; A value of 2 means there's only one desk for every two people.|
|USF Exclusions|array|Draw regions around areas intended to be excluded from USF calculation. This typically includes elevator shafts and stairwells for a full floor lease.|


<br>

|Output Name|Type|Description|
|---|---|---|
|Total Usable Floor Area|Number|The total usable floor area of the project.|
|Area per Person|Number|The usable floor area per person.|
|Total Desk Count|Number|The total number of desks.|
|Meeting room seats|Number|The total number of seats in the meeting rooms.|
|Classroom seats|Number|The total number of classroom seats.|
|Phone Booths|Number|Total number of Phone booths.|
|Collaboration seats|Number|Total seats in open collaboration areas.|
|Total Headcount|Number|The total number of employees and visitors accommodated.|
|Area per Desk|Number|The usable floor area per desk.|
|Desk Sharing Ratio|Number|How many people are there for each desk?|
|Meeting room ratio|Number|On average how many people does each meeting room serve? A value of 30 means there's one meeting room for every 30 people.|
|Private Office Count|Number|Total number of private offices.|

