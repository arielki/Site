﻿import { Component, ViewEncapsulation } from "@angular/core";
import { MapService } from "../services/map.service";
import { DataContainerService } from "../services/data-container.service";
import { ResourcesService } from "../services/resources.service";
import { FileService, IFormatViewModel } from "../services/file.service";
import { ToastService } from "../services/toast.service";
import { BaseMapComponent } from "./base-map.component";
import * as _ from "lodash";
import * as Common from "../common/IsraelHiking";

@Component({
    selector: "file-save-as",
    templateUrl: "./file-save-as.component.html",
    styleUrls: ["./file-save-as.component.css"],
    encapsulation: ViewEncapsulation.None
})
export class FileSaveAsComponent extends BaseMapComponent {

    public isOpen: boolean;
    public isFromatsDropdownOpen: boolean;
    public formats: IFormatViewModel[];
    public selectedFormat: IFormatViewModel;

    constructor(resources: ResourcesService,
        private mapService: MapService,
        private dataContainerService: DataContainerService,
        private fileService: FileService,
        private toastService: ToastService) {
        super(resources);

        this.isOpen = false;
        this.isFromatsDropdownOpen = false;
        this.formats = this.fileService.formats;
        this.selectedFormat = this.formats[0];  
    }

    public toggleSaveAs(e: Event) {
        this.isOpen = !this.isOpen;
        this.suppressEvents(e);
    };

    public saveAs = (format: IFormatViewModel, e: Event) => {
        this.selectedFormat = format;
        this.isFromatsDropdownOpen = false;
        let outputFormat = format.outputFormat;
        let data = this.dataContainerService.getDataForFileExport();
        if (outputFormat === "all_gpx_single_track") {
            outputFormat = "gpx_single_track";
            data = this.dataContainerService.getData();
        }
        if (!this.isDataSaveable(data)) {
            return;
        }
        let name = this.getName(data);
        this.fileService.saveToFile(`${name}.${format.extension}`, outputFormat, data)
            .then(() => { }, () => {
                this.toastService.error(this.resources.unableToSaveToFile);
            });

        this.isOpen = false;
        this.suppressEvents(e);
    }

    private getName(data: Common.DataContainer): string {
        let name = "IsraelHikingMap";
        if (data.routes.length === 1 && data.routes[0].name) {
            name = data.routes[0].name;
        }
        return name;
    }

    private isDataSaveable(data: Common.DataContainer): boolean {
        if (data.routes.length === 0) {
            this.toastService.warning(this.resources.unableToSaveAnEmptyRoute);
            return false;
        }
        if (_.every(data.routes, r => r.segments.length === 0 && r.markers.length === 0)) {
            this.toastService.warning(this.resources.unableToSaveAnEmptyRoute);
            return false;
        }
        return true;
    }
}
