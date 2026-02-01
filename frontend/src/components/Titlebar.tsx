import React, { useEffect, useState } from 'react';
import { Minus, Square, X } from 'lucide-react';
import { Quit, WindowMinimize, WindowToggleMaximize } from '@/api/bridge';
import { GetLauncherVersion } from '@/api/backend';
import appIcon from '../assets/appicon.svg';

export const Titlebar: React.FC = () => {
  const [version, setVersion] = useState<string>('');

  useEffect(() => {
    GetLauncherVersion().then(setVersion).catch(() => setVersion(''));
  }, []);

  return (
    <div 
      className="absolute top-0 left-0 right-0 h-10 flex items-center justify-between px-4 z-[100]"
      style={{ '--wails-draggable': 'drag' } as React.CSSProperties}
    >
      {/* Logo/Title */}
      <div className="flex items-center gap-3" style={{ '--wails-draggable': 'no-drag' } as React.CSSProperties}>
        <div className="w-6 h-6 flex items-center justify-center">
          <img src={appIcon} alt="HyPrism" className="w-full h-full object-contain" style={{ imageRendering: 'crisp-edges' }} />
        </div>
        <div className="flex items-center gap-2">
          <span className="text-white/80 text-sm font-semibold tracking-tight">HyPrism</span>
          {version && <span className="text-white/40 text-xs">v{version}</span>}
        </div>
      </div>

      {/* Window Controls */}
      <div className="flex items-center gap-1" style={{ '--wails-draggable': 'no-drag' } as React.CSSProperties}>
        <button
          onClick={() => WindowMinimize()}
          className="w-8 h-8 flex items-center justify-center rounded-md hover:bg-white/10 active:scale-95"
        >
          <Minus size={14} className="text-white/60" />
        </button>
        
        <button
          onClick={() => WindowToggleMaximize()}
          className="w-8 h-8 flex items-center justify-center rounded-md hover:bg-white/10 active:scale-95"
        >
          <Square size={12} className="text-white/60" />
        </button>
        
        <button
          onClick={() => Quit()}
          className="w-8 h-8 flex items-center justify-center rounded-md hover:bg-red-500/80 active:scale-95"
        >
          <X size={14} className="text-white/60 hover:text-white" />
        </button>
      </div>
    </div>
  );
};
