
LC_BUILD=$(top_builddir)/build/locale
#old automake versions don't set datarootdir or localedir
INSTALL_DIR=$(DESTDIR)$(prefix)/share/locale

FILES = $(addsuffix .po, $(ALL_LINGUAS))
GMO_FILES = $(patsubst %.po,$(LC_BUILD)/%/LC_MESSAGES/$(PACKAGE).mo,$(FILES))
MO_FILES = $(foreach po,$(FILES), $(INSTALL_DIR)/$(basename $(po))/LC_MESSAGES/$(PACKAGE).mo)

all: $(GMO_FILES)

update-po:
	$(MDTOOL_RUN) gettext-update -f:$(top_srcdir)/Main.sln

$(GMO_FILES): $(LC_BUILD)/%/LC_MESSAGES/$(PACKAGE).mo: %.po
	$(MKDIR_P) $(dir $@)
	msgfmt '$<' -o '$@'

statistics:
	@for LANGFILE in $(ALL_LINGUAS); do \
		echo "$$LANGFILE.po:"; \
		msgfmt -o /dev/null --verbose "$$LANGFILE.po"; \
	done

$(MO_FILES): $(INSTALL_DIR)/%/LC_MESSAGES/$(PACKAGE).mo : $(LC_BUILD)/%/LC_MESSAGES/$(PACKAGE).mo
	$(MKDIR_P) $(dir $@)
	$(install_sh) '$<' '$@'

install-data-local: $(MO_FILES)

uninstall-local:
	@for FILE in $(MO_FILES); do \
		if test -e "$$FILE"; then \
			echo "Removing $$FILE"; \
			rm "$$FILE"; \
		fi; \
	done

CLEANFILES = $(GMO_FILES)
EXTRA_DIST = Makefile $(FILES)

include $(top_srcdir)/Makefile.include
