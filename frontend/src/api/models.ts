/**
 * Application models and data structures
 * Generated from backend types
 */

export namespace app {
	/**
	 * Connectivity status information for external services
	 */
	export class ConnectivityInfo {
	    /** Whether Hytale patches server is reachable */
	    hytalePatches: boolean;
	    /** Whether GitHub API is reachable */
	    github: boolean;
	    /** Whether Itch.io is reachable */
	    itchIO: boolean;
	    /** Error message if connectivity check failed */
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

	/**
	 * Represents a game crash report file
	 */
	export class CrashReport {
	    /** Filename of the report */
	    filename: string;
	    /** Time when crash occurred */
	    timestamp: string;
	    /** Preview/snippet of the crash log */
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

	/**
	 * Status of required external dependencies
	 */
	export class DependenciesInfo {
	    /** Whether Java runtime is found */
	    javaInstalled: boolean;
	    /** Path to Java executable */
	    javaPath: string;
	    /** Whether Butler (itch.io) tool is found */
	    butlerInstalled: boolean;
	    /** Path to Butler executable */
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

	/**
	 * Current state of the game installation
	 */
	export class GameStatusInfo {
	    /** Whether game files are present */
	    installed: boolean;
	    /** Installed version identifier */
	    version: string;
	    /** Whether the main client executable exists */
	    clientExists: boolean;
	    /** Whether the online fix patch is applied */
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

	/**
	 * System platform information
	 */
	export class PlatformInfo {
	    /** Operating system name */
	    os: string;
	    /** CPU architecture */
	    arch: string;
	    /** OS version string */
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

	/**
	 * Comprehensive system diagnostic report
	 */
	export class DiagnosticReport {
	    /** Platform details */
	    platform: PlatformInfo;
	    /** Network connectivity status */
	    connectivity: ConnectivityInfo;
	    /** Game installation status */
	    gameStatus: GameStatusInfo;
	    /** Dependencies status */
	    dependencies: DependenciesInfo;
	    /** Report generation timestamp */
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
	
	/**
	 * Details of a locally installed game version
	 */
	export class InstalledVersion {
	    /** Version ID number */
	    version: number;
	    /** Branch type (e.g. Release, Beta) */
	    versionType: string;
	    /** Date the version was installed */
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
	
	/**
	 * Result of a version availability check
	 */
	export class VersionCheckInfo {
	    /** Whether the version is available for download */
	    available: boolean;
	    /** The version ID */
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
	/**
	 * Main application configuration
	 */
	export class Config {
	    /** Config file version */
	    version: string;
	    /** User nickname */
	    nick: string;
	    /** Whether background music is enabled */
	    musicEnabled: boolean;
	    /** Selected update branch type */
	    versionType: string;
	    /** ID of the currently selected version */
	    selectedVersion: number;
	    /** Custom path for game instances */
	    customInstanceDir: string;
	    /** Whether to automatically update the 'latest' instance */
	    autoUpdateLatest: boolean;
	    /** Whether online verification mode is enabled */
	    onlineMode: boolean;
	    /** Domain used for authentication services */
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
	/**
	 * Represents a file version of a mod
	 */
	export class ModFile {
	    /** Unique file ID */
	    id: number;
	    /** ID of the parent mod */
	    modId: number;
	    /** Display name of the file */
	    displayName: string;
	    /** Actual filename on disk */
	    fileName: string;
	    /** File size in bytes */
	    fileLength: number;
	    /** URL to download the file */
	    downloadUrl: string;
	    /** Date the file was uploaded/released */
	    fileDate: string;
	    /** Release type (Alpha/Beta/Release) */
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

	/**
	 * Author of a mod
	 */
	export class ModAuthor {
	    /** Author ID */
	    id: number;
	    /** Author name */
	    name: string;
	    /** Profile URL */
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

	/**
	 * Mod categorization tag
	 */
	export class ModCategory {
	    /** Category ID */
	    id: number;
	    /** Category display name */
	    name: string;
	    /** Category slug */
	    slug: string;
	    /** Category URL */
	    url: string;
	    /** Icon URL */
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

	/**
	 * Screenshot image associated with a mod
	 */
	export class ModScreenshot {
	    /** Image ID */
	    id: number;
	    /** Mod ID */
	    modId: number;
	    /** Image title */
	    title: string;
	    /** Image description */
	    description: string;
	    /** URL for thumbnail version */
	    thumbnailUrl: string;
	    /** URL for full version */
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

	/**
	 * Logo image for a mod
	 */
	export class ModLogo {
	    /** Image ID */
	    id: number;
	    /** Mod ID */
	    modId: number;
	    /** Logo title */
	    title: string;
	    /** Logo description */
	    description: string;
	    /** Thumbnail URL */
	    thumbnailUrl: string;
	    /** Full image URL */
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

	/**
	 * Detailed mod information from CurseForge
	 */
	export class CurseForgeMod {
	    /** CurseForge mod ID */
	    id: number;
	    /** Game ID */
	    gameId: number;
	    /** Mod display name */
	    name: string;
	    /** URL slug */
	    slug: string;
	    /** Short summary/description */
	    summary: string;
	    /** Total download count */
	    downloadCount: number;
	    /** Creation date string */
	    dateCreated: string;
	    /** Last modified date string */
	    dateModified: string;
	    /** Release date string */
	    dateReleased: string;
	    /** Mod logo image */
	    logo?: ModLogo;
	    /** List of screenshots */
	    screenshots: ModScreenshot[];
	    /** Categorization tags */
	    categories: ModCategory[];
	    /** List of authors */
	    authors: ModAuthor[];
	    /** Latest available files */
	    latestFiles: ModFile[];
	    /** ID of the main file */
	    mainFileId: number;
	    /** Whether distribution is allowed */
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

	/**
	 * Local representation of an installed mod
	 */
	export class Mod {
	    /** Mod ID */
	    id: string;
	    /** Display name */
	    name: string;
	    /** URL slug */
	    slug?: string;
	    /** Installed version */
	    version: string;
	    /** Author name */
	    author: string;
	    /** Description */
	    description: string;
	    /** Source download URL */
	    downloadUrl?: string;
	    /** Associated CurseForge project ID */
	    curseForgeId?: number;
	    /** Associated CurseForge file ID */
	    fileId?: number;
	    /** Whether the mod is enabled in the launcher */
	    enabled: boolean;
	    /** Installation timestamp */
	    installedAt: string;
	    /** Last update timestamp */
	    updatedAt: string;
	    /** Path to the mod file */
	    filePath: string;
	    /** Icon URL */
	    iconUrl?: string;
	    /** Download count */
	    downloads?: number;
	    /** Category name */
	    category?: string;
	    /** Latest available version string */
	    latestVersion?: string;
	    /** Latest available file ID */
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
	
	/**
	 * Result of a mod search query
	 */
	export class SearchResult {
	    /** List of found mods */
	    mods: CurseForgeMod[];
	    /** Total number of results matching query */
	    totalCount: number;
	    /** Current page index */
	    pageIndex: number;
	    /** Number of items per page */
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
	/**
	 * News item cover image
	 */
	export class coverImage {
	    /** S3 object key */
	    s3Key: string;
	
	    static createFrom(source: any = {}) {
	        return new coverImage(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.s3Key = source["s3Key"];
	    }
	}

	/**
	 * Represents a news article or announcement
	 */
	export class NewsItem {
	    /** Article title */
	    title: string;
	    /** Short body excerpt */
	    bodyExcerpt: string;
	    /** Brief summary */
	    excerpt: string;
	    /** Link to full article */
	    url: string;
	    /** Formatted date string */
	    date: string;
	    /** ISO 8601 publish date */
	    publishedAt: string;
	    /** URL slug */
	    slug: string;
	    /** Cover image object */
	    coverImage: coverImage;
	    /** Author name */
	    author: string;
	    /** Full URL to cover image */
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
	/**
	 * Represents a downloadable asset for an update
	 */
	export class Asset {
	    /** Download URL */
	    url: string;
	    /** SHA256 checksum of the file */
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

