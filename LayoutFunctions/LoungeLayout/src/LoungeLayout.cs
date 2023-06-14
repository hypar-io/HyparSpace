using Elements;
using Elements.Geometry;
using System.Collections.Generic;
using Elements.Components;
using System.Linq;
using System.IO;
using Newtonsoft.Json;
using System;
using Elements.Spatial;
using LayoutFunctionCommon;

namespace LoungeLayout
{
    public static class LoungeLayout
    {
        private static Dictionary<string, int> _countableSeatersByConfig = new Dictionary<string, int>()
        {
            ["Configuration A"] = 9,
            ["Configuration B"] = 4,
            ["Configuration C"] = 36,
            ["Configuration D"] = 8,
            ["Configuration E"] = 16,
            ["Configuration F"] = 13,
            ["Configuration G"] = 16,
            ["Configuration H"] = 9,
            ["Configuration I"] = 4,
            ["Configuration J"] = 18,
            ["Configuration K"] = 6,
            ["Configuration L"] = 3,
            ["Configuration M"] = 2,
        };

        private static Dictionary<string, int> _countableSeaters = new Dictionary<string, int>()
        {
            ["https://hypar.io/user-static/28ec56cc-3ae5-40df-a87f-9f116c1f9c62.glb"] = 1,
            ["https://hypar.io/user-static/2dcb44e3-71e5-4d50-93ba-c3abb259186f.glb"] = 1,
            ["https://hypar.io/user-static/31c06210-c409-4086-a496-1ec3093deddc.glb"] = 1,
            ["https://hypar.io/user-static/32534d22-4622-44ac-89d2-395f755ee4d9.glb"] = 1,
            ["https://hypar.io/user-static/47ba6d86-89ca-47b0-81e1-051919514ebd.glb"] = 1,
            ["https://hypar.io/user-static/48d2887c-543e-4b57-bc58-2cf4a132d5e7.glb"] = 1,
            ["https://hypar.io/user-static/75ccd3c4-4817-422f-86c5-f522ada7de37.glb"] = 1,
            ["https://hypar.io/user-static/8f5418c7-ad8b-474d-aa90-322f989f018c.glb"] = 1,
            ["https://hypar.io/user-static/93a62034-8f46-4312-a4be-bc043fe33206.glb"] = 1,
            ["https://hypar.io/user-static/a5c4260c-fd4e-4977-a4d0-a5f18ce283a2.glb"] = 1,
            ["https://hypar.io/user-static/b44b8d75-fc93-44e3-bca3-9c15502ffa25.glb"] = 1,
            ["https://hypar.io/user-static/c84d0092-962f-497e-ba1a-d5b25b61d1f4.glb"] = 1,
            ["https://hypar.io/user-static/d9dba349-e9e0-4d4e-a9b9-6e4652291f0c.glb"] = 1,
            ["https://hypar.io/user-static/f64bc3cf-7421-4b53-9fbc-92c95ac672cb.glb"] = 1,
            ["https://hypar.io/user-static/fb5ca161-397a-41f3-a469-532b61a43405.glb"] = 1,

            ["https://hypar.io/user-static/6f807770-c291-4baf-9f5e-36cd3714270b.glb"] = 2,
            ["https://hypar.io/user-static/e21bbd93-8800-4632-a0c7-5084a903598b.glb"] = 2,
            ["https://hypar.io/user-static/12f48254-237b-493a-8e09-7c44d1102c23.glb"] = 2,
            ["https://hypar.io/user-static/4b4a1da6-2ceb-4a19-92d4-21209756fb5d.glb"] = 2,
            ["https://hypar.io/user-static/537d3aad-7cb1-4a4c-af8f-3889e9becce7.glb"] = 2,
            ["https://hypar.io/user-static/7809959f-ae7f-4ea6-aa1b-4a2b609a2ae3.glb"] = 2,
            ["https://hypar.io/user-static/b1e7fd86-3124-4f9d-9e0d-650f453df774.glb"] = 2,
            ["https://hypar.io/user-static/cdc18dac-396b-474d-a20b-2b1670a22a1a.glb"] = 2,
        };

        /// <summary>
        /// The LoungeLayout function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A LoungeLayoutOutputs instance containing computed results and the model with any new elements.</returns>
        public static LoungeLayoutOutputs Execute(Dictionary<string, Model> inputModels, LoungeLayoutInputs input)
        {
            Elements.Serialization.glTF.GltfExtensions.UseReferencedContentExtension = true;
            var output = new LoungeLayoutOutputs();
            LayoutStrategies.StandardLayoutOnAllLevels<LevelElements, LevelVolume, SpaceBoundary, CirculationSegment>("Lounge", inputModels, input.Overrides, output.Model, false, "./LoungeConfigurations.json", default, CountSeats);

            return output;
        }

        private static int CountSeats(LayoutInstantiated layout)
        {
            return layout != null && _countableSeatersByConfig.TryGetValue(layout.ConfigName, out var seatsCount) && seatsCount > 0 ? seatsCount : 0;
        }
    }

}