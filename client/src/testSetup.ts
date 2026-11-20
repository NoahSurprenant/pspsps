// The prop designs and room looks the server starts with: what the browser reads from /api/props and /api/looks, for tests that draw rooms.
import designs from "../../src/Pspsps.Application/Rooms/PropDesigns.json?raw";
import looks from "../../src/Pspsps.Application/Rooms/RoomLooks.json?raw";
import { type LooksData, setLooks } from "./looks";
import { type PropDesign, setPropDesigns } from "./props";

setPropDesigns(JSON.parse(designs) as PropDesign[]);
setLooks(JSON.parse(looks) as LooksData);
