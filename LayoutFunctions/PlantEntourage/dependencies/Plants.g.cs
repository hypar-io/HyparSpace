using System;
using System.Collections.Generic;
using Elements.Geometry;
using Newtonsoft.Json;

namespace Elements {
public static class Plants {
    
    public static ContentElement DFlowersAndVase3DFlowersAndVase = new ContentElement( @"https://hypar.io/user-static/cef30794-0597-4ecd-8820-57394f480267.glb",
															new BBox3(new Vector3(-0.05921720212697983,-0.15525498561859133,-8.459899447643693E-18), new Vector3(0.4099599692344666,0.2420174222946167,0.6666704990386964)),
															1,
															new Vector3(0,1,0),
															null,
															new Transform(new Vector3(0,0,0),
																	new Vector3(1,0,0),
																	new Vector3(0,1,0),
																	new Vector3(0,0,1)),
															BuiltInMaterials.Default,
															null,
															true,
															new Guid("18eebb33-ceea-4d12-8022-b511c4939582"),
															@"3D_Flowers_and_vase - 3D_Flowers_and_vase",
															@"{""discriminator"":""Elements.ContentElement"",""Elevation from Level"":0.0,""Host"":""Level : Level 1"",""Offset from Host"":0.0,""Moves With Nearby Elements"":0,""HyFamilyTypeName"":""3D_Flowers_and_vase - 3D_Flowers_and_vase""}" );
    
    public static ContentElement DFlowerInVase3DFlowerInVase = new ContentElement( @"https://hypar.io/user-static/c16b7f33-6c7a-4844-9c3f-d63fa74050e0.glb",
															new BBox3(new Vector3(-0.1060121882915497,-0.07599115101099015,0.0004446767091751099), new Vector3(0.16846291851997378,0.07599114192724228,0.5174884348869324)),
															1,
															new Vector3(0,1,0),
															null,
															new Transform(new Vector3(0,0,0),
																	new Vector3(1,0,0),
																	new Vector3(0,1,0),
																	new Vector3(0,0,1)),
															BuiltInMaterials.Default,
															null,
															true,
															new Guid("97a349fd-3253-443c-b970-2c3bdc0a6034"),
															@"3D_Flower in vase - 3D_Flower in vase",
															@"{""discriminator"":""Elements.ContentElement"",""Elevation from Level"":0.0,""Host"":""Level : Level 1"",""Offset from Host"":0.0,""Moves With Nearby Elements"":0,""HyFamilyTypeName"":""3D_Flower in vase - 3D_Flower in vase""}" );
    

    public static List<ContentElement> All = new List<ContentElement> { 
                                                        DFlowersAndVase3DFlowersAndVase, 
                                                        DFlowerInVase3DFlowerInVase,  };
    public static List<ElementInstance> Reference = new List<ElementInstance> { 
                                                            Plants.DFlowersAndVase3DFlowersAndVase.CreateInstance(
                                                                                  new Transform(new Vector3(-9.32931089605092,-1.362668489336289,0),
																					new Vector3(1,0,0),
																					new Vector3(0,1,0),
																					new Vector3(0,0,1)),
                                                                                  @"3D_Flowers_and_vase - 3D_Flowers_and_vase"),
                                                        
                                                            Plants.DFlowerInVase3DFlowerInVase.CreateInstance(
                                                                                  new Transform(new Vector3(-10.266517476914675,-0.34383932838118875,0.076199999999992),
																					new Vector3(1,0,0),
																					new Vector3(0,1,0),
																					new Vector3(0,0,1)),
                                                                                  @"3D_Flower in vase - 3D_Flower in vase"),
                                                        

    };
  }
}