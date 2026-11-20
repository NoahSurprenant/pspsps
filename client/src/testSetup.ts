// The prop designs the server starts with: what the browser reads from /api/props, for tests that draw props.
import designs from "../../src/Pspsps.Application/Rooms/PropDesigns.json?raw";
import { type PropDesign, setPropDesigns } from "./props";

setPropDesigns(JSON.parse(designs) as PropDesign[]);
