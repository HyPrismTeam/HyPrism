import { useMemo, useState } from 'react';
import {
  Bug,
  Copy,
  Download,
  Edit,
  ExternalLink,
  FolderOpen,
  Play,
  RefreshCw,
  Save,
  Trash2,
  Upload,
  X,
} from 'lucide-react';
import {
  AccentSegmentedControl,
  Button,
  IconButton,
  LauncherActionButton,
  MenuActionButton,
  SelectionMark,
  Switch,
} from '@/components/ui/Controls';
import { SelectionCard } from '@/components/ui/SelectionCard';

type CatalogTab = 'launcher' | 'buttons' | 'tabs' | 'switches' | 'menus' | 'cards';

export function DeveloperUiCatalog() {
  const [tab, setTab] = useState<CatalogTab>('launcher');
  const [switchOn, setSwitchOn] = useState(true);
  const [selected, setSelected] = useState(true);

  const tabs = useMemo(
    () =>
      [
        { value: 'launcher' as const, label: 'Launcher' },
        { value: 'buttons' as const, label: 'Buttons' },
        { value: 'tabs' as const, label: 'Tabs' },
        { value: 'switches' as const, label: 'Switches' },
        { value: 'menus' as const, label: 'Menus' },
        { value: 'cards' as const, label: 'Cards' },
      ],
    []
  );

  return (
    <div className="space-y-4">
      <div className="glass-panel-static-solid rounded-2xl p-4">
        <div className="flex items-center justify-between">
          <div className="text-sm font-semibold text-white/80">UI Catalog</div>
          <div className="text-[10px] text-white/40">Developer tab</div>
        </div>

        <div className="mt-3">
          <AccentSegmentedControl<CatalogTab> value={tab} onChange={setTab} items={tabs} />
        </div>
      </div>

      {tab === 'launcher' ? (
        <div className="glass-panel-static-solid rounded-2xl p-4">
          <div className="text-xs font-semibold text-white/60">Launcher button samples</div>
          <div className="mt-3 flex flex-wrap items-center gap-2">
            <LauncherActionButton variant="play" className="h-10 px-5 text-sm">
              <Play className="w-4 h-4" /> Play
            </LauncherActionButton>
            <LauncherActionButton variant="download" className="h-10 px-5 text-sm">
              <Download className="w-4 h-4" /> Download
            </LauncherActionButton>
            <LauncherActionButton variant="update" className="h-10 px-5 text-sm">
              <RefreshCw className="w-4 h-4" /> Update
            </LauncherActionButton>
            <LauncherActionButton variant="stop" className="h-10 px-5 text-sm">
              <X className="w-4 h-4" /> Stop
            </LauncherActionButton>

            <Button size="sm">
              <FolderOpen className="w-4 h-4" /> Open folder
            </Button>
            <Button size="sm">
              <Edit className="w-4 h-4" /> Edit
            </Button>
            <Button size="sm">
              <Save className="w-4 h-4" /> Save
            </Button>
            <Button size="sm">
              <Upload className="w-4 h-4" /> Import
            </Button>
            <Button size="sm">
              <ExternalLink className="w-4 h-4" /> Open link
            </Button>
          </div>

          <div className="mt-4 text-xs font-semibold text-white/60">Icon-only actions</div>
          <div className="mt-2 flex flex-wrap items-center gap-2">
            <IconButton title="Refresh">
              <RefreshCw className="w-4 h-4" />
            </IconButton>
            <IconButton title="Copy">
              <Copy className="w-4 h-4" />
            </IconButton>
            <IconButton title="Folder">
              <FolderOpen className="w-4 h-4" />
            </IconButton>
          </div>
        </div>
      ) : null}

      {tab === 'buttons' ? (
        <div className="glass-panel-static-solid rounded-2xl p-4">
          <div className="text-xs font-semibold text-white/60">Primitives</div>
          <div className="mt-2 text-[11px] text-white/40">Button • IconButton</div>

          <div className="mt-3 flex flex-wrap items-center gap-2">
            <Button size="sm">Default</Button>
            <Button size="sm" variant="danger">Delete</Button>
            <IconButton title="IconButton">
              <Bug className="w-4 h-4" />
            </IconButton>
          </div>
        </div>
      ) : null}

      {tab === 'tabs' ? (
        <div className="glass-panel-static-solid rounded-2xl p-4">
          <div className="text-xs font-semibold text-white/60">Primitive</div>
          <div className="mt-2 text-[11px] text-white/40">AccentSegmentedControl</div>

          <div className="mt-3">
            <AccentSegmentedControl
              value={selected ? 'on' : 'off'}
              onChange={(v) => setSelected(v === 'on')}
              items={[
                { value: 'on' as const, label: 'On' },
                { value: 'off' as const, label: 'Off' },
                { value: 'disabled' as const, label: 'Disabled', disabled: true, title: 'Disabled example' },
              ]}
            />
          </div>
        </div>
      ) : null}

      {tab === 'switches' ? (
        <div className="glass-panel-static-solid rounded-2xl p-4">
          <div className="text-xs font-semibold text-white/60">Primitives</div>
          <div className="mt-2 text-[11px] text-white/40">Switch • SelectionMark</div>

          <div className="mt-3 flex flex-wrap items-center gap-4">
            <div className="flex items-center gap-3">
              <div className="text-xs text-white/50">Switch</div>
              <Switch checked={switchOn} onCheckedChange={setSwitchOn} />
            </div>

            <div className="flex items-center gap-3">
              <div className="text-xs text-white/50">SelectionMark</div>
              <SelectionMark selected={selected} className="border-white/50 text-white/80" />
              <Button size="sm" onClick={() => setSelected((s) => !s)}>
                Toggle
              </Button>
            </div>
          </div>
        </div>
      ) : null}

      {tab === 'menus' ? (
        <div className="glass-panel-static-solid rounded-2xl p-4">
          <div className="text-xs font-semibold text-white/60">Primitive</div>
          <div className="mt-2 text-[11px] text-white/40">MenuActionButton</div>

          <div className="mt-3 w-[260px] rounded-2xl overflow-hidden border border-white/15 bg-[#0a0a0a]/35">
            <MenuActionButton>
              <FolderOpen className="w-4 h-4" /> Open Folder
            </MenuActionButton>
            <div className="h-px bg-white/10" />
            <MenuActionButton>
              <Trash2 className="w-4 h-4" /> Delete
            </MenuActionButton>
          </div>
        </div>
      ) : null}

      {tab === 'cards' ? (
        <div className="glass-panel-static-solid rounded-2xl p-4">
          <div className="text-xs font-semibold text-white/60">Primitive</div>
          <div className="mt-2 text-[11px] text-white/40">SelectionCard</div>

          <div className="mt-3 grid grid-cols-2 gap-3">
            <SelectionCard
              icon={<Play className="w-5 h-5" />}
              title="SelectionCard"
              description="Selectable option card"
              selected
              onClick={() => {}}
            />
            <SelectionCard
              icon={<Trash2 className="w-5 h-5" />}
              title="Danger"
              description="Danger variant"
              variant="danger"
              onClick={() => {}}
            />
          </div>
        </div>
      ) : null}
    </div>
  );
}
