using Elements;
using Elements.Geometry;
using System.Collections.Generic;
using System.Linq;
using LayoutFunctionCommon;
using System;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("InteriorPartitions.Tests")]
namespace InteriorPartitions
{
    public static class InteriorPartitions
    {
        private static double defaultHeight = 3;

        /// <summary>
        /// The InteriorPartitions function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A InteriorPartitionsOutputs instance containing computed results and the model with any new elements.</returns>
        public static InteriorPartitionsOutputs Execute(Dictionary<string, Model> inputModels, InteriorPartitionsInputs input)
        {
            var interiorPartitionCandidates = new List<InteriorPartitionCandidate>();
            var modelDependencies = new[] {
                "Private Office Layout",
                "Phone Booth Layout",
                "Classroom Layout",
                "Meeting Room Layout",
                "Space Planning Zones",
                "Bedroom Layout",
                "Living Room Layout",
                "Kitchen Layout",
                "Workshop Layout",
                "Home Office Layout",
                "Bathroom Layout",
                "Restroom Layout",
                "Laundry Room Layout",
                "Entertainment Room Layout",
                "Room Layout",
                "Furniture and Equipment"
                 };
            foreach (var md in modelDependencies)
            {
                if (inputModels.TryGetValue(md, out var mdModel))
                {
                    interiorPartitionCandidates.AddRange(mdModel?.AllElementsOfType<InteriorPartitionCandidate>());
                }
            }

            var output = new InteriorPartitionsOutputs();
            var wallCandidates = CreateWallCandidates(input, interiorPartitionCandidates);
            output.Model.AddElements(wallCandidates);
            var wallCandidatesGroups = wallCandidates.GroupBy(w => (w.LevelTransform, w.Height));
            foreach (var wallCandidatesGroup in wallCandidatesGroups)
            {
                WallGeneration.GenerateWalls(output.Model, wallCandidatesGroup.Select(w => (w.Line, w.Type, w.Id, w.Thickness)), wallCandidatesGroup.Key.Height, wallCandidatesGroup.Key.LevelTransform);
            }
            return output;
        }

        internal static List<WallCandidate> CreateWallCandidates(InteriorPartitionsInputs input, List<InteriorPartitionCandidate> interiorPartitionCandidates)
        {
            // TODO: don't assume one height for all walls on a level — pass height through deduplication.
            var levelGroups = interiorPartitionCandidates.Where(c => c.WallCandidateLines.Count > 0).GroupBy(c => c.LevelTransform);
            var wallCandidates = new List<WallCandidate>();
            var userAddedWallLinesCandidates = new List<WallCandidate>();
            if (input.Overrides?.InteriorPartitionTypes != null)
            {
                userAddedWallLinesCandidates = input.Overrides.InteriorPartitionTypes.CreateElementsFromEdits(
                    input.Overrides.Additions.InteriorPartitionTypes,
                    (add) => new WallCandidate(add.Value.Line, add.Value.Type.ToString(), defaultHeight, new Transform()) { AddId = add.Id },
                    (wall, ident) => MatchIdentityWallCandidate(wall, ident),
                    (wall, edit) => UpdateWallCandidate(wall, edit)
                );
            }

            foreach (var levelGroup in levelGroups)
            {
                var candidates = WallGeneration.DeduplicateWallLines(levelGroup.ToList());
                var height = levelGroup.OrderBy(l => l.Height).FirstOrDefault()?.Height ?? defaultHeight;
                var levelWallCandidates = candidates.Select(c =>
                    new WallCandidate(c.Line.TransformedLine(levelGroup.Key),
                                      c.Type,
                                      height,
                                      levelGroup.Key,
                                      new List<SpaceBoundary>())
                    {
                        Thickness = c.Thickness
                    });
                if (input.Overrides?.InteriorPartitionTypes != null)
                {
                    levelWallCandidates = UpdateLevelWallCandidates(levelWallCandidates, input.Overrides.InteriorPartitionTypes, input.Overrides.Removals.InteriorPartitionTypes);
                }

                var splittedCandidates = WallGeneration.SplitOverlappingWallCandidates(
                    levelWallCandidates.Select(w => (w.Line, w.Type)),
                    userAddedWallLinesCandidates.Select(w => (w.Line.TransformedLine(w.LevelTransform), w.Type)));
                var splittedWallCandidates = splittedCandidates
                    .Select(c => new WallCandidate(c.Line, c.Type, height, levelGroup.Key, new List<SpaceBoundary>()))
                    .ToList();

                wallCandidates.AddRange(splittedWallCandidates);
            }
            AttachOverrides(input.Overrides.InteriorPartitionTypes, wallCandidates, input.Overrides.Additions.InteriorPartitionTypes);
            RemoveWallCandidates(input.Overrides.Removals.InteriorPartitionTypes, wallCandidates);

            return wallCandidates;
        }

        private static bool MatchIdentityWallCandidate(WallCandidate wallCandidate, InteriorPartitionTypesIdentity ident)
        {
            var isLinesEqual = ident.Line.IsAlmostEqualTo(wallCandidate.Line, false, 0.1);
            return ident.AddId?.Equals(wallCandidate.AddId?.ToString()) == true && isLinesEqual;
        }

        private static WallCandidate UpdateWallCandidate(WallCandidate wallCandidate, InteriorPartitionTypesOverride edit)
        {
            wallCandidate.Line = edit.Value.Line ?? wallCandidate.Line;
            wallCandidate.Type = edit.Value.Type.ToString();
            return wallCandidate;
        }

        public static List<WallCandidate> CreateElementsFromEdits(
            this IList<InteriorPartitionTypesOverride> edits,
            IList<InteriorPartitionTypesOverrideAddition> additions,
            Func<InteriorPartitionTypesOverrideAddition, WallCandidate> createElement,
            Func<WallCandidate, InteriorPartitionTypesIdentity, bool> identityMatch,
            Func<WallCandidate, InteriorPartitionTypesOverride, WallCandidate> modifyElement)
        {
            var resultElements = new List<WallCandidate>();
            if (additions != null)
            {
                foreach (var addedElement in additions)
                {
                    var elementToAdd = createElement(addedElement);
                    resultElements.Add(elementToAdd);
                    Identity.AddOverrideIdentity(elementToAdd, addedElement);
                }
            }
            if (edits != null)
            {
                foreach (var editedElement in edits)
                {
                    var elementToEdit = resultElements.FirstOrDefault(e => identityMatch(e, editedElement.Identity));
                    if (elementToEdit != null)
                    {
                        resultElements.Remove(elementToEdit);
                        var newElement = modifyElement(elementToEdit, editedElement);
                        resultElements.Add(newElement);
                        Identity.AddOverrideIdentity(newElement, editedElement);
                    }
                    else
                    {
                        var newElement = new WallCandidate(editedElement.Value.Line, editedElement.Value.Type.ToString(), defaultHeight, new Transform(), null);
                        resultElements.Add(newElement);
                        Identity.AddOverrideIdentity(newElement, editedElement);
                    }
                }
            }
            return resultElements;
        }

        private static List<WallCandidate> UpdateLevelWallCandidates(
            IEnumerable<WallCandidate> levelWallCandidates,
            IList<InteriorPartitionTypesOverride> edits,
            IList<InteriorPartitionTypesOverrideRemoval> removals)
        {
            var resultElements = new List<WallCandidate>(levelWallCandidates);
            if (removals != null)
            {
                foreach (var removedElement in removals)
                {
                    var elementToRemove = resultElements.FirstOrDefault(e => removedElement.Identity.Line.IsAlmostEqualTo(e.Line, false, 0.1));
                    if (elementToRemove != null)
                    {
                        resultElements.Remove(elementToRemove);
                    }
                }
            }
            if (edits != null)
            {
                foreach (var editedElement in edits)
                {
                    WallCandidate overlappingWallCandidate = null;
                    var identityLine = editedElement.Identity.Line;
                    foreach (var wallCandidate in resultElements)
                    {
                        if (!wallCandidate.Line.IsCollinear(identityLine))
                        {
                            continue;
                        }

                        // check if secondLine lies inside firstLine
                        if (!Line.PointOnLine(identityLine.Start, wallCandidate.Line.Start, wallCandidate.Line.End, true)
                            || !Line.PointOnLine(identityLine.End, wallCandidate.Line.Start, wallCandidate.Line.End, true))
                        {
                            continue;
                        }

                        overlappingWallCandidate = wallCandidate;
                        break;
                    }

                    if (overlappingWallCandidate != null)
                    {
                        var overlappingLine = overlappingWallCandidate.Line;
                        var vectors = new List<Vector3>() { overlappingLine.Start, overlappingLine.End, identityLine.Start, identityLine.End };
                        var direction = overlappingLine.Direction();
                        var orderedVectors = vectors.OrderBy(v => (v - overlappingLine.Start).Dot(direction)).ToList();

                        resultElements.Remove(overlappingWallCandidate);
                        if (!orderedVectors[0].IsAlmostEqualTo(orderedVectors[1]))
                        {
                            resultElements.Add(new WallCandidate(new Line(orderedVectors[0], orderedVectors[1]), overlappingWallCandidate.Type, overlappingWallCandidate.Height, overlappingWallCandidate.LevelTransform));
                        }

                        resultElements.Add(new WallCandidate(editedElement.Value.Line, editedElement.Value.Type.ToString(), overlappingWallCandidate.Height, overlappingWallCandidate.LevelTransform));

                        if (!orderedVectors[2].IsAlmostEqualTo(orderedVectors[3]))
                        {
                            resultElements.Add(new WallCandidate(new Line(orderedVectors[2], orderedVectors[3]), overlappingWallCandidate.Type, overlappingWallCandidate.Height, overlappingWallCandidate.LevelTransform));
                        }
                    }
                }
            }
            return resultElements;
        }

        public static void AttachOverrides(this IList<InteriorPartitionTypesOverride> overrideData, IEnumerable<WallCandidate> existingElements, IList<InteriorPartitionTypesOverrideAddition> additions)
        {
            if (overrideData != null)
            {
                foreach (var overrideValue in overrideData)
                {
                    var matchingElement = existingElements.FirstOrDefault(e => overrideValue.Value.Line.IsAlmostEqualTo(e.Line, false, 0.01));
                    if (matchingElement != null)
                    {
                        matchingElement.Type = overrideValue.Value.Type.ToString();
                        Identity.AddOverrideIdentity(matchingElement, overrideValue);

                        var addOverride = additions?.FirstOrDefault(a => a.Id.Equals(overrideValue.Identity.AddId));
                        if (addOverride != null)
                        {
                            Identity.AddOverrideIdentity(matchingElement, addOverride);
                            SetAddIdForContainedElements(existingElements, overrideValue.Value.Line, addOverride);
                        }
                    }
                }
            }
            if (additions != null)
            {
                var elementsWithoutIdentities = existingElements.Where(e => !e.AdditionalProperties.ContainsKey("associatedIdentities"));
                foreach (var addedElement in additions)
                {
                    var matchingElement = elementsWithoutIdentities.FirstOrDefault(e => addedElement.Value.Line.IsAlmostEqualTo(e.Line, false, 0.01));
                    if (matchingElement != null)
                    {
                        Identity.AddOverrideIdentity(matchingElement, addedElement);
                    }
                    SetAddIdForContainedElements(existingElements, addedElement.Value.Line, addedElement);
                }
            }
        }

        private static void SetAddIdForContainedElements(IEnumerable<WallCandidate> existingElements, Line overrideLine, InteriorPartitionTypesOverrideAddition addOverride)
        {
            var containedElements = existingElements.Where(e => Line.PointOnLine(e.Line.Start, overrideLine.Start, overrideLine.End, true, 0.01)
                                                                && Line.PointOnLine(e.Line.End, overrideLine.Start, overrideLine.End, true, 0.01));
            foreach (var containedElement in containedElements)
            {
                containedElement.AddId = addOverride.Id;
            }
        }

        private static void RemoveWallCandidates(IList<InteriorPartitionTypesOverrideRemoval> removals, List<WallCandidate> wallCandidates)
        {
            if (removals != null)
            {
                foreach (var removedElement in removals)
                {
                    var elementToRemove = wallCandidates.FirstOrDefault(e => MatchIdentityWallCandidate(e, removedElement.Identity));
                    if (elementToRemove != null)
                    {
                        wallCandidates.Remove(elementToRemove);
                    }
                }
            }
        }
    }
}