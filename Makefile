.PHONY: all build clean run
null  :=
space := $(null) #
comma := ,
join_comma = $(subst $(space),$(comma),$(1))

all: build

###############################################################################
S = src/KCKSeFCli
SOURCES := $(shell find $(S) \( -path $(S)/obj -o -path $(S)/bin \) -prune -o \( -type f \( -name '*.cs' -o -name '*.csproj' \) -print \) )
B = $(S)/obj
$(B)/init: ./.gitmodules
	git submodule update --init --recursive
	@mkdir -p $(dir $@) && touch $@
$(B)/build: $(B)/init $(SOURCES)
	dotnet build $(S)
	@mkdir -p $(dir $@) && touch $@
$(B)/format: $(SOURCES)
	dotnet format $(S) -v d
	@mkdir -p $(dir $@) && touch $@
###############################################################################

.PHONY: build format run test-format clean sources install-hooks
build: $(B)/build
format: $(B)/format
sources:
	@echo $(SOURCES)
run: build
	dotnet run --project $(S) --
# No `test` target on purpose: it depended on `format`, so running the tests rewrote ~30 source
# files as a side effect. Run dotnet test against tests/KCKSeFCli.Tests/ directly instead.
clean:
	dotnet clean $(S)
	rm $(B)/build $(B)/format $(B)/init
test-format:
	dotnet format $(S) -v d --verify-no-changes
# core.hooksPath is local config, not versioned, so a fresh clone has no commit-msg hook until
# this runs. Worktrees share .git/config, so one install covers all of them.
install-hooks:
	git config core.hooksPath .githooks

###############################################################################

.PHONY: nix-fix
nix-fix:
	for f in $$(find src/KCKSeFCli/bin dist out out-self -type f -executable -name kcksefcli 2>/dev/null); do \
		echo "Patching $$f..."; \
		patchelf --remove-rpath "$$f"; \
		patchelf --set-interpreter /lib64/ld-linux-x86-64.so.2 "$$f"; \
	done
