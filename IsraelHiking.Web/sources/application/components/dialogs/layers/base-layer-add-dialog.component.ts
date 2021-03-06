﻿import { Component } from "@angular/core";
import { LayerBaseDialogComponent } from "./layer-base-dialog.component";
import { ResourcesService } from "../../../services/resources.service";
import { MapService } from "../../../services/map.service";
import { ToastService } from "../../../services/toast.service";
import { LayersService } from "../../../services/layers/layers.service";
import * as Common from "../../../common/IsraelHiking";

@Component({
    selector: "baselayer-add-dialog",
    templateUrl: "./layer-properties-dialog.component.html"
})
export class BaseLayerAddDialogComponent extends LayerBaseDialogComponent {
    constructor(resources: ResourcesService,
        layersService: LayersService,
        mapService: MapService,
        toastService: ToastService,
        ) {
        super(resources, mapService, layersService, toastService);
        this.title = this.resources.addBaseLayer;
        this.isNew = true;
    }

    protected internalSave(layerData: Common.LayerData): string {
        let layer = this.layersService.addBaseLayer(layerData);
        this.layersService.selectBaseLayer(layer);
        return "";
    };
}