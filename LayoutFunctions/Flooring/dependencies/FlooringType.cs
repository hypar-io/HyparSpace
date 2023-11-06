using Elements;
using System;
using System.Linq;
using System.Collections.Generic;
using Elements.Geometry;
using Newtonsoft.Json;
using Elements.Geometry.Solids;
using Flooring;

namespace Elements
{
    // This portion of the FlooringType class is yours to edit with your own element behaviors.
    public partial class FlooringType
    {
        /// <summary>
        /// Construct a new instance of the element.
        /// </summary>
        /// <param name="add">User input at add time.</param>
        public FlooringType(FloorTypesOverrideAddition add)
        {
            // Optionally customize this method.
            this.SetAllProperties(add);
            this.Material = new Material(this.Name, this.Color ?? Colors.Magenta);
        }


        /// <summary>
        /// Update the element on a subsequent change.
        /// </summary>
        /// <param name="edit">User input at edit time.</param>
        public void Update(FloorTypesOverride edit)
        {
            // Optionally customize this method.
            this.SetAllProperties(edit);
            this.Material.Color = this.Color ?? this.Material.Color;
        }

        public double TextureSize { get; set; } = 1.0;

        public static FlooringType Terazzo => new()
        {
            Thickness = 0.01,
            Color = "#ffffff",
            Id = new Guid("3b8acb47-094f-4af9-bda6-c0c037fcf022"),
            Name = "Terazzo",
            Material = new Material("Terazzo")
            {
                Color = "#ffffff",
                Texture = "./Textures/terrazzo_3-1K/Terrazzo_3_basecolor-1K.png",
                NormalTexture = "./Textures/terrazzo_3-1K/Terrazzo_3_normal-1K.png",
                RepeatTexture = true,
                SpecularFactor = 0.2,
                GlossinessFactor = 0.5,
            }
        };
        public static FlooringType Wood => new()
        {
            Thickness = 0.01,
            Color = "#ffffff",
            Id = new Guid("d93016bb-5985-4b2a-bc21-014f6a9c4250"),
            Name = "Wood",
            Material = new Material("Wood")
            {
                Color = "#ffffff",
                Texture = "./Textures/wood_floor_4k.blend/wood_floor_diff_4k.png",
                RepeatTexture = true,
                SpecularFactor = 0.2,
                GlossinessFactor = 0.2,
            }
        };

        public static FlooringType Laminate => new()
        {
            Thickness = 0.01,
            Color = "#ffffff",
            Id = new Guid("dad03f4d-4a83-4afb-a600-7b852ef747e9"),
            Name = "Laminate",
            Material = new Material("Laminate")
            {
                Color = "#ffffff",
                Texture = "./Textures/laminate_floor_02_4k.blend/laminate_floor_02_diff_4k.png",
                RepeatTexture = true,
                SpecularFactor = 0.0, // Adjust as per requirements
                GlossinessFactor = 0.5, // Adjust as per requirements
            }
        };

        public static FlooringType Tile => new()
        {
            Thickness = 0.01,
            Color = "#ffffff",
            Id = new Guid("f178d15e-6a79-420e-a999-2e849d8de944"),
            Name = "Tile",
            Material = new Material("Tile")
            {
                Color = "#ffffff",
                Texture = "./Textures/tiling_20-1K/1K_tiling_20_basecolor.png",
                NormalTexture = "./Textures/tiling_20-1K/1K_tiling_20_normal.png",
                RepeatTexture = true,
                SpecularFactor = 0.5, // Adjust as per requirements
                GlossinessFactor = 0.5, // Adjust as per requirements
            }
        };

        public static FlooringType WoodParquet => new()
        {
            Thickness = 0.01,
            Color = "#ffffff",
            Id = new Guid("a20aa825-ebf3-43c7-92f4-27ab96e6b227"),
            Name = "Wood Parquet",
            Material = new Material("Wood Parquet")
            {
                Color = "#ffffff",
                Texture = "./Textures/woodparquet_88-1K/woodparquet_88_basecolor-1K.png",
                NormalTexture = "./Textures/woodparquet_88-1K/woodparquet_88_normal-1K.png",
                RepeatTexture = true,
                SpecularFactor = 0.0, // Adjust as per requirements
                GlossinessFactor = 0.5, // Adjust as per requirements
            }
        };

        public static FlooringType LightWood => new()
        {
            Thickness = 0.01,
            Color = "#ffffff",
            Id = new Guid("c1c412d8-25be-4871-94f4-4de3e7b285e1"),
            Name = "Light Wood",
            Material = new Material("Light Wood")
            {
                Color = "#ffffff",
                Texture = "./Textures/woodparquet_88-1K/woodparquet_88_basecolor-warm-1K.png",
                NormalTexture = "./Textures/woodparquet_88-1K/woodparquet_88_normal-1K.png",
                RepeatTexture = true,
                SpecularFactor = 0.0, // Adjust as per requirements
                GlossinessFactor = 0.5, // Adjust as per requirements
            }
        };

        public static FlooringType Carpet => new()
        {
            Thickness = 0.01,
            Color = "#393f55",
            Id = new Guid("19d69bfb-d5b5-4281-82c6-167b40f2dbda"),
            Name = "Carpet",
            Material = new Material("Carpet")
            {
                Color = "#393f55",
                Texture = "./Textures/Carpet012_1K-JPG/Carpet012_1K-JPG_Color-Neutral.png",
                NormalTexture = "./Textures/Carpet012_1K-JPG/Carpet012_1K-JPG_NormalGL.png",
                RepeatTexture = true,
                SpecularFactor = 0.2, // Adjust as per requirements
                GlossinessFactor = 0.3, // Adjust as per requirements
            }
        };

        public static FlooringType Vinyl => new() {
            Thickness = 0.01,
            Color = "#999999",
            Id = new Guid("71e71bbe-5f9e-42b4-8c72-ab6c1cc05bd6"),
            Name = "Vinyl",
            Material = new Material("Vinyl")
            {
                Color = "#999999",
                SpecularFactor = 0.5, // Adjust as per requirements
                GlossinessFactor = 0.5, // Adjust as per requirements
            }
        };
    }
}