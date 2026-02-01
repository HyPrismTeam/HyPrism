export namespace app {
	export class ConnectivityInfo {
	    hytalePatches: boolean;
	    github: boolean;
	    itchIO: boolean;
	    error?: string;
	
	    static createFrom(source: any = {}) {
	        return new ConnectivityInfo(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.hytalePatches = source["hytalePatches"];
	        this.github = source["github"];
	        this.itchIO = source["itchIO"];
	        this.error = source["error"];
	    }
	}

	export class CrashReport {
	    filename: string;
	    timestamp: string;
	    preview: string;
	
	    static createFrom(source: any = {}) {
	        return new CrashReport(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.filename = source["filename"];
	        this.timestamp = source["timestamp"];
	        this.preview = source["preview"];
	    }
	}

	export class DependenciesInfo {
	    javaInstalled: boolean;
	    javaPath: string;
	    butlerInstalled: boolean;
	    butlerPath: string;
	
	    static createFrom(source: any = {}) {
	        return new DependenciesInfo(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.javaInstalled = source["javaInstalled"];
	        this.javaPath = source["javaPath"];
	        this.butlerInstalled = source["butlerInstalled"];
	        this.butlerPath = source["butlerPath"];
	    }
	}

	export class GameStatusInfo {
	    installed: boolean;
	    version: string;
	    clientExists: boolean;
	    onlineFixApplied: boolean;
	
	    static createFrom(source: any = {}) {
	        return new GameStatusInfo(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.installed = source["installed"];
	        this.version = source["version"];
	        this.clientExists = source["clientExists"];
	        this.onlineFixApplied = source["onlineFixApplied"];
	    }
	}

	export class PlatformInfo {
	    os: string;
	    arch: string;
	    version: string;
	
	    static createFrom(source: any = {}) {
	        return new PlatformInfo(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.os = source["os"];
	        this.arch = source["arch"];
	        this.version = source["version"];
	    }
	}

	export class DiagnosticReport {
	    platform: PlatformInfo;
	    connectivity: ConnectivityInfo;
	    gameStatus: GameStatusInfo;
	    dependencies: DependenciesInfo;
	    timestamp: string;
	
	    static createFrom(source: any = {}) {
	        return new DiagnosticReport(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.platform = this.convertValues(source["platform"], PlatformInfo);
	        this.connectivity = this.convertValues(source["connectivity"], ConnectivityInfo);
	        this.gameStatus = this.convertValues(source["gameStatus"], GameStatusInfo);
	        this.dependencies = this.convertValues(source["dependencies"], DependenciesInfo);
	        this.timestamp = source["timestamp"];
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}
	
	export class InstalledVersion {
	    version: number;
	    versionType: string;
	    installDate: string;
	
	    static createFrom(source: any = {}) {
	        return new InstalledVersion(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.version = source["version"];
	        this.versionType = source["versionType"];
	        this.installDate = source["installDate"];
	    }
	}
	
	export class VersionCheckInfo {
	    available: boolean;
	    version: number;
	
	    static createFrom(source: any = {}) {
	        return new VersionCheckInfo(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.available = source["available"];
	        this.version = source["version"];
	    }
	}

}

export namespace config {
	export class Config {
	    version: string;
	    nick: string;
	    musicEnabled: boolean;
	    versionType: string;
	    selectedVersion: number;
	    customInstanceDir: string;
	    autoUpdateLatest: boolean;
	    onlineMode: boolean;
	    authDomain: string;
	
	    static createFrom(source: any = {}) {
	        return new Config(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.version = source["version"];
	        this.nick = source["nick"];
	        this.musicEnabled = source["musicEnabled"];
	        this.versionType = source["versionType"];
	        this.selectedVersion = source["selectedVersion"];
	        this.customInstanceDir = source["customInstanceDir"];
	        this.autoUpdateLatest = source["autoUpdateLatest"];
	        this.onlineMode = source["onlineMode"];
	        this.authDomain = source["authDomain"];
	    }
	}

}

export namespace mods {
	export class ModFile {
	    id: number;
	    modId: number;
	    displayName: string;
	    fileName: string;
	    fileLength: number;
	    downloadUrl: string;
	    fileDate: string;
	    releaseType: number;
	
	    static createFrom(source: any = {}) {
	        return new ModFile(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.modId = source["modId"];
	        this.displayName = source["displayName"];
	        this.fileName = source["fileName"];
	        this.fileLength = source["fileLength"];
	        this.downloadUrl = source["downloadUrl"];
	        this.fileDate = source["fileDate"];
	        this.releaseType = source["releaseType"];
	    }
	}

	export class ModAuthor {
	    id: number;
	    name: string;
	    url: string;
	
	    static createFrom(source: any = {}) {
	        return new ModAuthor(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.name = source["name"];
	        this.url = source["url"];
	    }
	}

	export class ModCategory {
	    id: number;
	    name: string;
	    slug: string;
	    url: string;
	    iconUrl: string;
	
	    static createFrom(source: any = {}) {
	        return new ModCategory(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.name = source["name"];
	        this.slug = source["slug"];
	        this.url = source["url"];
	        this.iconUrl = source["iconUrl"];
	    }
	}

	export class ModScreenshot {
	    id: number;
	    modId: number;
	    title: string;
	    description: string;
	    thumbnailUrl: string;
	    url: string;
	
	    static createFrom(source: any = {}) {
	        return new ModScreenshot(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.modId = source["modId"];
	        this.title = source["title"];
	        this.description = source["description"];
	        this.thumbnailUrl = source["thumbnailUrl"];
	        this.url = source["url"];
	    }
	}

	export class ModLogo {
	    id: number;
	    modId: number;
	    title: string;
	    description: string;
	    thumbnailUrl: string;
	    url: string;
	
	    static createFrom(source: any = {}) {
	        return new ModLogo(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.modId = source["modId"];
	        this.title = source["title"];
	        this.description = source["description"];
	        this.thumbnailUrl = source["thumbnailUrl"];
	        this.url = source["url"];
	    }
	}

	export class CurseForgeMod {
	    id: number;
	    gameId: number;
	    name: string;
	    slug: string;
	    summary: string;
	    downloadCount: number;
	    dateCreated: string;
	    dateModified: string;
	    dateReleased: string;
	    logo?: ModLogo;
	    screenshots: ModScreenshot[];
	    categories: ModCategory[];
	    authors: ModAuthor[];
	    latestFiles: ModFile[];
	    mainFileId: number;
	    allowModDistribution: boolean;
	
	    static createFrom(source: any = {}) {
	        return new CurseForgeMod(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.gameId = source["gameId"];
	        this.name = source["name"];
	        this.slug = source["slug"];
	        this.summary = source["summary"];
	        this.downloadCount = source["downloadCount"];
	        this.dateCreated = source["dateCreated"];
	        this.dateModified = source["dateModified"];
	        this.dateReleased = source["dateReleased"];
	        this.logo = this.convertValues(source["logo"], ModLogo);
	        this.screenshots = this.convertValues(source["screenshots"], ModScreenshot);
	        this.categories = this.convertValues(source["categories"], ModCategory);
	        this.authors = this.convertValues(source["authors"], ModAuthor);
	        this.latestFiles = this.convertValues(source["latestFiles"], ModFile);
	        this.mainFileId = source["mainFileId"];
	        this.allowModDistribution = source["allowModDistribution"];
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}

	export class Mod {
	    id: string;
	    name: string;
	    slug?: string;
	    version: string;
	    author: string;
	    description: string;
	    downloadUrl?: string;
	    curseForgeId?: number;
	    fileId?: number;
	    enabled: boolean;
	    installedAt: string;
	    updatedAt: string;
	    filePath: string;
	    iconUrl?: string;
	    downloads?: number;
	    category?: string;
	    latestVersion?: string;
	    latestFileId?: number;
	
	    static createFrom(source: any = {}) {
	        return new Mod(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.name = source["name"];
	        this.slug = source["slug"];
	        this.version = source["version"];
	        this.author = source["author"];
	        this.description = source["description"];
	        this.downloadUrl = source["downloadUrl"];
	        this.curseForgeId = source["curseForgeId"];
	        this.fileId = source["fileId"];
	        this.enabled = source["enabled"];
	        this.installedAt = source["installedAt"];
	        this.updatedAt = source["updatedAt"];
	        this.filePath = source["filePath"];
	        this.iconUrl = source["iconUrl"];
	        this.downloads = source["downloads"];
	        this.category = source["category"];
	        this.latestVersion = source["latestVersion"];
	        this.latestFileId = source["latestFileId"];
	    }
	}
	
	export class SearchResult {
	    mods: CurseForgeMod[];
	    totalCount: number;
	    pageIndex: number;
	    pageSize: number;
	
	    static createFrom(source: any = {}) {
	        return new SearchResult(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.mods = this.convertValues(source["mods"], CurseForgeMod);
	        this.totalCount = source["totalCount"];
	        this.pageIndex = source["pageIndex"];
	        this.pageSize = source["pageSize"];
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}

}

export namespace news {
	export class coverImage {
	    s3Key: string;
	
	    static createFrom(source: any = {}) {
	        return new coverImage(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.s3Key = source["s3Key"];
	    }
	}
	export class NewsItem {
	    title: string;
	    bodyExcerpt: string;
	    excerpt: string;
	    url: string;
	    date: string;
	    publishedAt: string;
	    slug: string;
	    coverImage: coverImage;
	    author: string;
	    imageUrl: string;
	
	    static createFrom(source: any = {}) {
	        return new NewsItem(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.title = source["title"];
	        this.bodyExcerpt = source["bodyExcerpt"];
	        this.excerpt = source["excerpt"];
	        this.url = source["url"];
	        this.date = source["date"];
	        this.publishedAt = source["publishedAt"];
	        this.slug = source["slug"];
	        this.coverImage = this.convertValues(source["coverImage"], coverImage);
	        this.author = source["author"];
	        this.imageUrl = source["imageUrl"];
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}

}

export namespace updater {
	export class Asset {
	    url: string;
	    sha256: string;
	
	    static createFrom(source: any = {}) {
	        return new Asset(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.url = source["url"];
	        this.sha256 = source["sha256"];
	    }
	}

}

