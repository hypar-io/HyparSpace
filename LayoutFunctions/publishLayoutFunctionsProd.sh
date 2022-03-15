#!/bin/bash
projects=(
    "ClassroomLayout"
    "DataHall"
    "LoungeLayout"
    "MeetingRoomLayout"
    "OpenCollabLayout"
    "OpenOfficeLayout"
    "PantryLayout"
    "PhoneBoothLayout"
    "PrivateOfficeLayout"
    "ReceptionLayout"
)

task() {
echo $project
cd ./$project/
hypar publish --stage --disable-pull-check
cd ../ 
}

for project in ${projects[@]};
do
    task $project &
done
wait