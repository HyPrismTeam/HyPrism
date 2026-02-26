import { h } from 'preact';

export type LucideIcon = (props: any) => h.JSX.Element;

const IconBase = ({ children, size = 24, color = 'currentColor', className = '', ...props }: any) => (
  <svg xmlns="http://www.w3.org/2000/svg" width={size} height={size} viewBox="0 0 24 24" fill="none" stroke={color} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className={className} {...props}>
    {children}
  </svg>
);

export const AlertCircle = (props: any) => (
  <IconBase {...props}>
    <circle cx="12" cy="12" r="10" key="1mglay" />
    <line x1="12" x2="12" y1="8" y2="12" key="1pkeuh" />
    <line x1="12" x2="12.01" y1="16" y2="16" key="4dfq90" />
  </IconBase>
);

export const AlertTriangle = (props: any) => (
  <IconBase {...props}>
    <path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3Z" key="c3ski4" />
    <path d="M12 9v4" key="juzpu7" />
    <path d="M12 17h.01" key="p32p05" />
  </IconBase>
);

export const ArrowLeft = (props: any) => (
  <IconBase {...props}>
    <path d="m12 19-7-7 7-7" key="1l729n" />
    <path d="M19 12H5" key="x3x0zl" />
  </IconBase>
);

export const ArrowRight = (props: any) => (
  <IconBase {...props}>
    <path d="M5 12h14" key="1ays0h" />
    <path d="m12 5 7 7-7 7" key="xquz4c" />
  </IconBase>
);

export const Box = (props: any) => (
  <IconBase {...props}>
    <path d="M21 8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16Z" key="hh9hay" />
    <path d="m3.3 7 8.7 5 8.7-5" key="g66t2b" />
    <path d="M12 22V12" key="d0xqtd" />
  </IconBase>
);

export const Bug = (props: any) => (
  <IconBase {...props}>
    <path d="m8 2 1.88 1.88" key="fmnt4t" />
    <path d="M14.12 3.88 16 2" key="qol33r" />
    <path d="M9 7.13v-1a3.003 3.003 0 1 1 6 0v1" key="d7y7pr" />
    <path d="M12 20c-3.3 0-6-2.7-6-6v-3a4 4 0 0 1 4-4h4a4 4 0 0 1 4 4v3c0 3.3-2.7 6-6 6" key="xs1cw7" />
    <path d="M12 20v-9" key="1qisl0" />
    <path d="M6.53 9C4.6 8.8 3 7.1 3 5" key="32zzws" />
    <path d="M6 13H2" key="82j7cp" />
    <path d="M3 21c0-2.1 1.7-3.9 3.8-4" key="4p0ekp" />
    <path d="M20.97 5c0 2.1-1.6 3.8-3.5 4" key="18gb23" />
    <path d="M22 13h-4" key="1jl80f" />
    <path d="M17.2 17c2.1.1 3.8 1.9 3.8 4" key="k3fwyw" />
  </IconBase>
);

export const Calendar = (props: any) => (
  <IconBase {...props}>
    <rect width="18" height="18" x="3" y="4" rx="2" ry="2" key="eu3xkr" />
    <line x1="16" x2="16" y1="2" y2="6" key="m3sa8f" />
    <line x1="8" x2="8" y1="2" y2="6" key="18kwsl" />
    <line x1="3" x2="21" y1="10" y2="10" key="xt86sb" />
  </IconBase>
);

export const Check = (props: any) => (
  <IconBase {...props}>
    <path d="M20 6 9 17l-5-5" key="1gmf2c" />
  </IconBase>
);

export const CheckCircle = (props: any) => (
  <IconBase {...props}>
    <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14" key="g774vq" />
    <path d="m9 11 3 3L22 4" key="1pflzl" />
  </IconBase>
);

export const ChevronDown = (props: any) => (
  <IconBase {...props}>
    <path d="m6 9 6 6 6-6" key="qrunsl" />
  </IconBase>
);

export const ChevronLeft = (props: any) => (
  <IconBase {...props}>
    <path d="m15 18-6-6 6-6" key="1wnfg3" />
  </IconBase>
);

export const ChevronRight = (props: any) => (
  <IconBase {...props}>
    <path d="m9 18 6-6-6-6" key="mthhwq" />
  </IconBase>
);

export const ChevronUp = (props: any) => (
  <IconBase {...props}>
    <path d="m18 15-6-6-6 6" key="153udz" />
  </IconBase>
);

export const Coffee = (props: any) => (
  <IconBase {...props}>
    <path d="M17 8h1a4 4 0 1 1 0 8h-1" key="jx4kbh" />
    <path d="M3 8h14v9a4 4 0 0 1-4 4H7a4 4 0 0 1-4-4Z" key="1bxrl0" />
    <line x1="6" x2="6" y1="2" y2="4" key="1cr9l3" />
    <line x1="10" x2="10" y1="2" y2="4" key="170wym" />
    <line x1="14" x2="14" y1="2" y2="4" key="1c5f70" />
  </IconBase>
);

export const Copy = (props: any) => (
  <IconBase {...props}>
    <rect width="14" height="14" x="8" y="8" rx="2" ry="2" key="17jyea" />
    <path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2" key="zix9uf" />
  </IconBase>
);

export const CopyPlus = (props: any) => (
  <IconBase {...props}>
    <line x1="15" x2="15" y1="12" y2="18" key="1p7wdc" />
    <line x1="12" x2="18" y1="15" y2="15" key="1nscbv" />
    <rect width="14" height="14" x="8" y="8" rx="2" ry="2" key="17jyea" />
    <path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2" key="zix9uf" />
  </IconBase>
);

export const Cpu = (props: any) => (
  <IconBase {...props}>
    <rect x="4" y="4" width="16" height="16" rx="2" key="1vbyd7" />
    <rect x="9" y="9" width="6" height="6" key="o3kz5p" />
    <path d="M15 2v2" key="13l42r" />
    <path d="M15 20v2" key="15mkzm" />
    <path d="M2 15h2" key="1gxd5l" />
    <path d="M2 9h2" key="1bbxkp" />
    <path d="M20 15h2" key="19e6y8" />
    <path d="M20 9h2" key="19tzq7" />
    <path d="M9 2v2" key="165o2o" />
    <path d="M9 20v2" key="i2bqo8" />
  </IconBase>
);

export const Dices = (props: any) => (
  <IconBase {...props}>
    <rect width="12" height="12" x="2" y="10" rx="2" ry="2" key="6agr2n" />
    <path d="m17.92 14 3.5-3.5a2.24 2.24 0 0 0 0-3l-5-4.92a2.24 2.24 0 0 0-3 0L10 6" key="1o487t" />
    <path d="M6 18h.01" key="uhywen" />
    <path d="M10 14h.01" key="ssrbsk" />
    <path d="M15 6h.01" key="cblpky" />
    <path d="M18 9h.01" key="2061c0" />
  </IconBase>
);

export const Download = (props: any) => (
  <IconBase {...props}>
    <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" key="ih7n3h" />
    <polyline points="7 10 12 15 17 10" key="2ggqvy" />
    <line x1="12" x2="12" y1="15" y2="3" key="1vk2je" />
  </IconBase>
);

export const DownloadCloud = (props: any) => (
  <IconBase {...props}>
    <path d="M4 14.899A7 7 0 1 1 15.71 8h1.79a4.5 4.5 0 0 1 2.5 8.242" key="1pljnt" />
    <path d="M12 12v9" key="192myk" />
    <path d="m8 17 4 4 4-4" key="1ul180" />
  </IconBase>
);

export const Edit3 = (props: any) => (
  <IconBase {...props}>
    <path d="M12 20h9" key="t2du7b" />
    <path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4Z" key="ymcmye" />
  </IconBase>
);

export const ExternalLink = (props: any) => (
  <IconBase {...props}>
    <path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6" key="a6xqqp" />
    <polyline points="15 3 21 3 21 9" key="mznyad" />
    <line x1="10" x2="21" y1="14" y2="3" key="18c3s4" />
  </IconBase>
);

export const FlaskConical = (props: any) => (
  <IconBase {...props}>
    <path d="M10 2v7.527a2 2 0 0 1-.211.896L4.72 20.55a1 1 0 0 0 .9 1.45h12.76a1 1 0 0 0 .9-1.45l-5.069-10.127A2 2 0 0 1 14 9.527V2" key="pzvekw" />
    <path d="M8.5 2h7" key="csnxdl" />
    <path d="M7 16h10" key="wp8him" />
  </IconBase>
);

export const FolderOpen = (props: any) => (
  <IconBase {...props}>
    <path d="m6 14 1.5-2.9A2 2 0 0 1 9.24 10H20a2 2 0 0 1 1.94 2.5l-1.54 6a2 2 0 0 1-1.95 1.5H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h3.9a2 2 0 0 1 1.69.9l.81 1.2a2 2 0 0 0 1.67.9H18a2 2 0 0 1 2 2v2" key="usdka0" />
  </IconBase>
);

export const GitBranch = (props: any) => (
  <IconBase {...props}>
    <line x1="6" x2="6" y1="3" y2="15" key="17qcm7" />
    <circle cx="18" cy="6" r="3" key="1h7g24" />
    <circle cx="6" cy="18" r="3" key="fqmcym" />
    <path d="M18 9a9 9 0 0 1-9 9" key="n2h4wq" />
  </IconBase>
);

export const Github = (props: any) => (
  <IconBase {...props}>
    <path d="M15 22v-4a4.8 4.8 0 0 0-1-3.5c3 0 6-2 6-5.5.08-1.25-.27-2.48-1-3.5.28-1.15.28-2.35 0-3.5 0 0-1 0-3 1.5-2.64-.5-5.36-.5-8 0C6 2 5 2 5 2c-.3 1.15-.3 2.35 0 3.5A5.403 5.403 0 0 0 4 9c0 3.5 3 5.5 6 5.5-.39.49-.68 1.05-.85 1.65-.17.6-.22 1.23-.15 1.85v4" key="tonef" />
    <path d="M9 18c-4.51 2-5-2-7-2" key="9comsn" />
  </IconBase>
);

export const Globe = (props: any) => (
  <IconBase {...props}>
    <circle cx="12" cy="12" r="10" key="1mglay" />
    <path d="M12 2a14.5 14.5 0 0 0 0 20 14.5 14.5 0 0 0 0-20" key="13o1zl" />
    <path d="M2 12h20" key="9i4pu4" />
  </IconBase>
);

export const HardDrive = (props: any) => (
  <IconBase {...props}>
    <line x1="22" x2="2" y1="12" y2="12" key="1y58io" />
    <path d="M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z" key="oot6mr" />
    <line x1="6" x2="6.01" y1="16" y2="16" key="sgf278" />
    <line x1="10" x2="10.01" y1="16" y2="16" key="1l4acy" />
  </IconBase>
);

export const Home = (props: any) => (
  <IconBase {...props}>
    <path d="m3 9 9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z" key="y5dka4" />
    <polyline points="9 22 9 12 15 12 15 22" key="e2us08" />
  </IconBase>
);

export const Image = (props: any) => (
  <IconBase {...props}>
    <rect width="18" height="18" x="3" y="3" rx="2" ry="2" key="1m3agn" />
    <circle cx="9" cy="9" r="2" key="af1f0g" />
    <path d="m21 15-3.086-3.086a2 2 0 0 0-2.828 0L6 21" key="1xmnt7" />
  </IconBase>
);

export const Info = (props: any) => (
  <IconBase {...props}>
    <circle cx="12" cy="12" r="10" key="1mglay" />
    <path d="M12 16v-4" key="1dtifu" />
    <path d="M12 8h.01" key="e9boi3" />
  </IconBase>
);

export const Loader2 = (props: any) => (
  <IconBase {...props}>
    <path d="M21 12a9 9 0 1 1-6.219-8.56" key="13zald" />
  </IconBase>
);

export const Lock = (props: any) => (
  <IconBase {...props}>
    <rect width="18" height="11" x="3" y="11" rx="2" ry="2" key="1w4ew1" />
    <path d="M7 11V7a5 5 0 0 1 10 0v4" key="fwvmzm" />
  </IconBase>
);

export const LogIn = (props: any) => (
  <IconBase {...props}>
    <path d="M15 3h4a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-4" key="u53s6r" />
    <polyline points="10 17 15 12 10 7" key="1ail0h" />
    <line x1="15" x2="3" y1="12" y2="12" key="v6grx8" />
  </IconBase>
);

export const Monitor = (props: any) => (
  <IconBase {...props}>
    <rect width="20" height="14" x="2" y="3" rx="2" key="48i651" />
    <line x1="8" x2="16" y1="21" y2="21" key="1svkeh" />
    <line x1="12" x2="12" y1="17" y2="21" key="vw1qmm" />
  </IconBase>
);

export const Newspaper = (props: any) => (
  <IconBase {...props}>
    <path d="M4 22h16a2 2 0 0 0 2-2V4a2 2 0 0 0-2-2H8a2 2 0 0 0-2 2v16a2 2 0 0 1-2 2Zm0 0a2 2 0 0 1-2-2v-9c0-1.1.9-2 2-2h2" key="7pis2x" />
    <path d="M18 14h-8" key="sponae" />
    <path d="M15 18h-5" key="95g1m2" />
    <path d="M10 6h8v4h-8V6Z" key="smlsk5" />
  </IconBase>
);

export const Palette = (props: any) => (
  <IconBase {...props}>
    <circle cx="13.5" cy="6.5" r=".5" key="1xcu5" />
    <circle cx="17.5" cy="10.5" r=".5" key="736e4u" />
    <circle cx="8.5" cy="7.5" r=".5" key="clrty" />
    <circle cx="6.5" cy="12.5" r=".5" key="1s4xz9" />
    <path d="M12 2C6.5 2 2 6.5 2 12s4.5 10 10 10c.926 0 1.648-.746 1.648-1.688 0-.437-.18-.835-.437-1.125-.29-.289-.438-.652-.438-1.125a1.64 1.64 0 0 1 1.668-1.668h1.996c3.051 0 5.555-2.503 5.555-5.554C21.965 6.012 17.461 2 12 2z" key="12rzf8" />
  </IconBase>
);

export const Play = (props: any) => (
  <IconBase {...props}>
    <polygon points="5 3 19 12 5 21 5 3" key="191637" />
  </IconBase>
);

export const Plus = (props: any) => (
  <IconBase {...props}>
    <path d="M5 12h14" key="1ays0h" />
    <path d="M12 5v14" key="s699le" />
  </IconBase>
);

export const Power = (props: any) => (
  <IconBase {...props}>
    <path d="M12 2v10" key="mnfbl" />
    <path d="M18.4 6.6a9 9 0 1 1-12.77.04" key="obofu9" />
  </IconBase>
);

export const RefreshCw = (props: any) => (
  <IconBase {...props}>
    <path d="M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8" key="v9h5vc" />
    <path d="M21 3v5h-5" key="1q7to0" />
    <path d="M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16" key="3uifl3" />
    <path d="M8 16H3v5" key="1cv678" />
  </IconBase>
);

export const RotateCcw = (props: any) => (
  <IconBase {...props}>
    <path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8" key="1357e3" />
    <path d="M3 3v5h5" key="1xhq8a" />
  </IconBase>
);

export const Search = (props: any) => (
  <IconBase {...props}>
    <circle cx="11" cy="11" r="8" key="4ej97u" />
    <path d="m21 21-4.3-4.3" key="1qie3q" />
  </IconBase>
);

export const Server = (props: any) => (
  <IconBase {...props}>
    <rect width="20" height="8" x="2" y="2" rx="2" ry="2" key="ngkwjq" />
    <rect width="20" height="8" x="2" y="14" rx="2" ry="2" key="iecqi9" />
    <line x1="6" x2="6.01" y1="6" y2="6" key="16zg32" />
    <line x1="6" x2="6.01" y1="18" y2="18" key="nzw8ys" />
  </IconBase>
);

export const Settings = (props: any) => (
  <IconBase {...props}>
    <path d="M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z" key="1qme2f" />
    <circle cx="12" cy="12" r="3" key="1v7zrd" />
  </IconBase>
);

export const Shield = (props: any) => (
  <IconBase {...props}>
    <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10" key="1irkt0" />
  </IconBase>
);

export const ShieldAlert = (props: any) => (
  <IconBase {...props}>
    <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10" key="1irkt0" />
    <path d="M12 8v4" key="1got3b" />
    <path d="M12 16h.01" key="1drbdi" />
  </IconBase>
);

export const SkipForward = (props: any) => (
  <IconBase {...props}>
    <polygon points="5 4 15 12 5 20 5 4" key="16p6eg" />
    <line x1="19" x2="19" y1="5" y2="19" key="futhcm" />
  </IconBase>
);

export const Sparkles = (props: any) => (
  <IconBase {...props}>
    <path d="m12 3-1.912 5.813a2 2 0 0 1-1.275 1.275L3 12l5.813 1.912a2 2 0 0 1 1.275 1.275L12 21l1.912-5.813a2 2 0 0 1 1.275-1.275L21 12l-5.813-1.912a2 2 0 0 1-1.275-1.275L12 3Z" key="17u4zn" />
    <path d="M5 3v4" key="bklmnn" />
    <path d="M19 17v4" key="iiml17" />
    <path d="M3 5h4" key="nem4j1" />
    <path d="M17 19h4" key="lbex7p" />
  </IconBase>
);

export const Terminal = (props: any) => (
  <IconBase {...props}>
    <polyline points="4 17 10 11 4 5" key="akl6gq" />
    <line x1="12" x2="20" y1="19" y2="19" key="q2wloq" />
  </IconBase>
);

export const Trash2 = (props: any) => (
  <IconBase {...props}>
    <path d="M3 6h18" key="d0wm0j" />
    <path d="M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6" key="4alrt4" />
    <path d="M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2" key="v07s0e" />
    <line x1="10" x2="10" y1="11" y2="17" key="1uufr5" />
    <line x1="14" x2="14" y1="11" y2="17" key="xtxkd" />
  </IconBase>
);

export const User = (props: any) => (
  <IconBase {...props}>
    <path d="M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2" key="975kel" />
    <circle cx="12" cy="7" r="4" key="17ys0d" />
  </IconBase>
);

export const Users = (props: any) => (
  <IconBase {...props}>
    <path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2" key="1yyitq" />
    <circle cx="9" cy="7" r="4" key="nufk8" />
    <path d="M22 21v-2a4 4 0 0 0-3-3.87" key="kshegd" />
    <path d="M16 3.13a4 4 0 0 1 0 7.75" key="1da9ce" />
  </IconBase>
);

export const Volume2 = (props: any) => (
  <IconBase {...props}>
    <polygon points="11 5 6 9 2 9 2 15 6 15 11 19 11 5" key="16drj5" />
    <path d="M15.54 8.46a5 5 0 0 1 0 7.07" key="ltjumu" />
    <path d="M19.07 4.93a10 10 0 0 1 0 14.14" key="1kegas" />
  </IconBase>
);

export const VolumeX = (props: any) => (
  <IconBase {...props}>
    <polygon points="11 5 6 9 2 9 2 15 6 15 11 19 11 5" key="16drj5" />
    <line x1="22" x2="16" y1="9" y2="15" key="1ewh16" />
    <line x1="16" x2="22" y1="9" y2="15" key="5ykzw1" />
  </IconBase>
);

export const Wifi = (props: any) => (
  <IconBase {...props}>
    <path d="M5 13a10 10 0 0 1 14 0" key="6v8j51" />
    <path d="M8.5 16.5a5 5 0 0 1 7 0" key="sej527" />
    <path d="M2 8.82a15 15 0 0 1 20 0" key="dnpr2z" />
    <line x1="12" x2="12.01" y1="20" y2="20" key="of4bc4" />
  </IconBase>
);

export const X = (props: any) => (
  <IconBase {...props}>
    <path d="M18 6 6 18" key="1bl5f8" />
    <path d="m6 6 12 12" key="d8bk6v" />
  </IconBase>
);

export const XCircle = (props: any) => (
  <IconBase {...props}>
    <circle cx="12" cy="12" r="10" key="1mglay" />
    <path d="m15 9-6 6" key="1uzhvr" />
    <path d="m9 9 6 6" key="z0biqf" />
  </IconBase>
);

export const Upload = (props: any) => (
  <IconBase {...props}>
    <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
    <polyline points="17 8 12 3 7 8" />
    <line x1="12" y1="3" x2="12" y2="15" />
  </IconBase>
);

export const Clock = (props: any) => (
  <IconBase {...props}>
    <circle cx="12" cy="12" r="10" />
    <polyline points="12 6 12 12 16 14" />
  </IconBase>
);

export const Code = (props: any) => (
  <IconBase {...props}>
    <polyline points="16 18 22 12 16 6" />
    <polyline points="8 6 2 12 8 18" />
  </IconBase>
);

export const Database = (props: any) => (
  <IconBase {...props}>
    <ellipse cx="12" cy="5" rx="9" ry="3" />
    <path d="M21 12c0 1.66-4 3-9 3s-9-1.34-9-3" />
    <path d="M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5" />
  </IconBase>
);

export const Edit = (props: any) => (
  <IconBase {...props}>
    <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" />
    <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z" />
  </IconBase>
);

export const Edit2 = (props: any) => (
  <IconBase {...props}>
    <path d="M17 3a2.828 2.828 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5L17 3z" />
  </IconBase>
);

export const FileText = (props: any) => (
  <IconBase {...props}>
    <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
    <polyline points="14 2 14 8 20 8" />
    <line x1="16" y1="13" x2="8" y2="13" />
    <line x1="16" y1="17" x2="8" y2="17" />
    <polyline points="10 9 9 9 8 9" />
  </IconBase>
);

export const Map = (props: any) => (
  <IconBase {...props}>
    <polygon points="1 6 1 22 8 18 16 22 23 18 23 2 16 6 8 2 1 6" />
    <line x1="8" y1="2" x2="8" y2="18" />
    <line x1="16" y1="6" x2="16" y2="22" />
  </IconBase>
);

export const MoreVertical = (props: any) => (
  <IconBase {...props}>
    <circle cx="12" cy="5" r="1" />
    <circle cx="12" cy="12" r="1" />
    <circle cx="12" cy="19" r="1" />
  </IconBase>
);

export const Package = (props: any) => (
  <IconBase {...props}>
    <line x1="16.5" y1="9.4" x2="7.5" y2="4.21" />
    <path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z" />
    <polyline points="3.27 6.96 12 12.01 20.73 6.96" />
    <line x1="12" y1="22.08" x2="12" y2="12" />
  </IconBase>
);

export const RotateCw = (props: any) => (
  <IconBase {...props}>
    <polyline points="23 4 23 10 17 10" />
    <path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10" />
  </IconBase>
);

export const Save = (props: any) => (
  <IconBase {...props}>
    <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z" />
    <polyline points="17 21 17 13 7 13 7 21" />
    <polyline points="7 3 7 8 15 8" />
  </IconBase>
);
